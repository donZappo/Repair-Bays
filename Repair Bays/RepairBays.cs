using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BattleTech;
using BattleTech.UI;
using Harmony;
using Newtonsoft.Json;
using static RepairBays.Logger;

namespace RepairBays
{
    public class RepairBays
    {
        public static int[] bays;
        public static int bcount;
        public static string modDirectory;
        public static Settings modSettings = new Settings();

        public static void Init(string modDir, string modSettings)
        {
            HarmonyInstance harmonyInstance = HarmonyInstance.Create("com.battletech.repairbays");
            harmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
            modDirectory = modDir;
            try
            {
                RepairBays.modSettings = JsonConvert.DeserializeObject<Settings>(modSettings);
                if (RepairBays.modSettings.enableDebug)
                {
                    LogClear();
                }
            }
            catch (Exception e)
            {
                LogError(e);
                RepairBays.modSettings = new Settings();
            }
        }

        public class Settings
        {
            public bool enableDebug;
        }
    }

    [HarmonyPatch(typeof(SimGameState), "UpdateMechLabWorkQueue", MethodType.Normal)]
    public static class SimGameState_UpdateMechLabWorkQueue
    {
        private static void Prefix(SimGameState __instance, bool passDay)
        {
            var instance = __instance;

            // don't do anything at all if the queue is empty or time hasn't passed
            if (instance.MechLabQueue.Count < 1 || !passDay) return;

            // have the automation 1 or automation 2 techs been researched?
            bool auto1 = false;
            bool auto2 = false;
            if (instance.shipUpgrades.Any(u => u.Tags.Any(t => t.Contains("argo_mechBay_automation1"))))
                auto1 = true;
            if (instance.shipUpgrades.Any(u => u.Tags.Any(t => t.Contains("argo_mechBay_automation2"))))
                auto2 = true;

            // how many pods are there?
            int numMechBayPods = instance.CompanyStats.GetValue<int>(__instance.Constants.Story.MechBayPodsID);

            // if there's a second pod and second item in the queue, do work
            if ((instance.MechLabQueue.Count >= 2) && (numMechBayPods >= 2))
            {
                float corfact = 0.5f;
                if (auto1)
                    corfact = 1;
                instance.MechLabQueue[1].PayCost(Convert.ToInt32(corfact * instance.MechTechSkill));
            }

            // if there's a third pod and third item in the queue, do work
            if ((instance.MechLabQueue.Count >= 3) && (numMechBayPods >= 3))
            {
                float corfact = 0.3333333f;
                if (auto2)
                    corfact = 1;
                instance.MechLabQueue[2].PayCost(Convert.ToInt32(corfact * instance.MechTechSkill));
            }
        }
    }

    [HarmonyPatch(typeof(TaskTimelineWidget), "RefreshEntries", MethodType.Normal)]
    public static class TaskTimelineWidget_RefreshEntries
    {
        private static void Prefix(TaskTimelineWidget __instance, SimGameState ___Sim)
        {
            //LogDebug($"TaskTimelineWidget Prefix");

            //bays = new int[3];
            //bcount = ___Sim.CompanyStats.GetValue<int>(___Sim.Constants.Story.MechBayPodsID);
        }

        public static void Postfix(TaskTimelineWidget __instance, SimGameState ___Sim)
        {
            // has the automation 1 or automation 2 techs been researched?
            bool auto1 = false;
            bool auto2 = false;
            if (___Sim.shipUpgrades.Any(u => u.Tags.Any(t => t.Contains("argo_mechBay_automation1"))))
                auto1 = true;
            if (___Sim.shipUpgrades.Any(u => u.Tags.Any(t => t.Contains("argo_mechBay_automation2"))))
                auto2 = true;

            // how many pods are there?
            int numMechBayPods = ___Sim.CompanyStats.GetValue<int>(___Sim.Constants.Story.MechBayPodsID);

            // how efficient are they (if they exist)?
            double efficiencyBay1 = 1.0;
            double efficiencyBay2 = (numMechBayPods < 2) ? 0.0 : (auto1 ? 1.0 : 0.5);
            double efficiencyBay3 = (numMechBayPods < 3) ? 0.0 : (auto2 ? 1.0 : 0.33333333);
            // mech points per bay per day
            int workRateBay1 = Convert.ToInt32(efficiencyBay1 * ___Sim.MechTechSkill);
            int workRateBay2 = Convert.ToInt32(efficiencyBay2 * ___Sim.MechTechSkill);
            int workRateBay3 = Convert.ToInt32(efficiencyBay3 * ___Sim.MechTechSkill);

            // get an array containing only the mechtech jobs, and another with work remaining for each
            List<WorkOrderEntry> workOrdersTemp = new List<WorkOrderEntry>();
            List<int> workRemainingTemp = new List<int>();
            foreach (WorkOrderEntry workOrderEntry in ___Sim.MechLabQueue)
            {
                // sanity checks (make sure we're looking at a mechtech job with widgit)
                if (!___Sim.WorkOrderIsMechTech(workOrderEntry.Type))
                    continue;
                TaskManagementElement taskManagementElement = null;
                if (!__instance.ActiveItems.TryGetValue(workOrderEntry, out taskManagementElement))
                    continue;
                // it's the right type of thing
                workOrdersTemp.Add(workOrderEntry);
                workRemainingTemp.Add(workOrderEntry.GetRemainingCost());
            }
            WorkOrderEntry[] workOrders = workOrdersTemp.ToArray();
            int[] workRemaining = workRemainingTemp.ToArray();


            // design thoughts for following algorithm:
            // taskManagementElement by default has its own time, plus days passed according to position in queue
            // taskManagementElement.UpdateItem(x); overrides that behaviour to have its own time, plus x (ignoring queue)
            // it returns that number

            int workOrdersCount = workOrders.Length;
            int indexTaskBay1 = (workOrdersCount >= 1) ? 0 : -1;
            int indexTaskBay2 = (workOrdersCount >= 2) ? 1 : -1;
            int indexTaskBay3 = (workOrdersCount >= 3) ? 2 : -1;
            int cumulativeDays = 0;

            // caution, the following loop assumes bay 1 has a strictly positive work rate
            int loopLimit = 100;
            while (indexTaskBay1 != -1)
            {
                // if something awful happens, like a non-positive work rate in bay 1, just give up after a while!
                if (--loopLimit < 0)
                    return;

                // find the minimal number of days to finish a task (in bay 1, 2, or 3)
                int daysProgress = 0;
                int days1 = -1;
                int days2 = -1;
                int days3 = -1;
                if ((workRateBay1 > 0) && (workRemaining[indexTaskBay1] > 0))
                    days1 = Convert.ToInt32(Math.Ceiling(workRemaining[indexTaskBay1] / (double)workRateBay1));
                if (indexTaskBay2 != -1)
                    if ((workRateBay2 > 0) && (workRemaining[indexTaskBay2] > 0))
                        days2 = Convert.ToInt32(Math.Ceiling(workRemaining[indexTaskBay2] / (double)workRateBay2));
                if (indexTaskBay3 != -1)
                    if ((workRateBay3 > 0) && (workRemaining[indexTaskBay3] > 0))
                        days3 = Convert.ToInt32(Math.Ceiling(workRemaining[indexTaskBay3] / (double)workRateBay3));
                if (days1 >= 0)
                    daysProgress = days1;
                if ((days2 >= 0) && (days2 < daysProgress))
                    daysProgress = days2;
                if ((days3 >= 0) && (days3 < daysProgress))
                    daysProgress = days3;

                // progress tasks in all three bays (if they exist) by the minimal number of days
                cumulativeDays += daysProgress;
                workRemaining[indexTaskBay1] -= daysProgress * workRateBay1;
                if (indexTaskBay2 != -1)
                    workRemaining[indexTaskBay2] -= daysProgress * workRateBay2;
                if (indexTaskBay3 != -1)
                    workRemaining[indexTaskBay3] -= daysProgress * workRateBay3;

                // this next bit is very ugly, very repetitive...

                // if task 3 exists and is finished, move on
                if ((indexTaskBay3 != -1) && (workRemaining[indexTaskBay3] <= 0))
                {
                    WorkOrderEntry workOrderEntry = ___Sim.MechLabQueue[indexTaskBay3];
                    TaskManagementElement taskManagementElement = null;
                    // sanity checks
                    if (!__instance.ActiveItems.TryGetValue(workOrderEntry, out taskManagementElement))
                        continue;
                    if (!___Sim.WorkOrderIsMechTech(workOrderEntry.Type))
                        continue;
                    // how long was it supposed to take
                    int expectedLength = taskManagementElement.UpdateItem(0);
                    // update with a start time as if it had finished at cumulativeDays
                    taskManagementElement.UpdateItem(cumulativeDays - expectedLength);
                    // update task indicies
                    indexTaskBay3 += 1;
                    if (indexTaskBay3 >= workOrdersCount)
                        indexTaskBay3 = -1;
                }
                // if task 2 exists and is finished, move on
                if ((indexTaskBay2 != -1) && (workRemaining[indexTaskBay2] <= 0))
                {
                    WorkOrderEntry workOrderEntry = ___Sim.MechLabQueue[indexTaskBay2];
                    TaskManagementElement taskManagementElement = null;
                    // sanity checks
                    if (!___Sim.WorkOrderIsMechTech(workOrderEntry.Type))
                        continue;
                    if (!__instance.ActiveItems.TryGetValue(workOrderEntry, out taskManagementElement))
                        continue;
                    // how long was it supposed to take
                    int expectedLength = taskManagementElement.UpdateItem(0);
                    // update with a start time as if it had finished at cumulativeDays
                    taskManagementElement.UpdateItem(cumulativeDays - expectedLength);
                    // update task indicies
                    indexTaskBay2 = indexTaskBay3;
                    if (indexTaskBay3 != -1)
                        indexTaskBay3 += 1;
                    if (indexTaskBay3 >= workOrdersCount)
                        indexTaskBay3 = -1;
                }
                // if task 1 is finished, move on
                if (workRemaining[indexTaskBay1] <= 0)
                {
                    WorkOrderEntry workOrderEntry = ___Sim.MechLabQueue[indexTaskBay1];
                    TaskManagementElement taskManagementElement = null;
                    // sanity checks
                    if (!___Sim.WorkOrderIsMechTech(workOrderEntry.Type))
                        continue;
                    if (!__instance.ActiveItems.TryGetValue(workOrderEntry, out taskManagementElement))
                        continue;
                    // how long was it supposed to take
                    int expectedLength = taskManagementElement.UpdateItem(0);
                    // update with a start time as if it had finished at cumulativeDays
                    taskManagementElement.UpdateItem(cumulativeDays - expectedLength);
                    // update task indicies
                    indexTaskBay1 = indexTaskBay2;
                    indexTaskBay2 = indexTaskBay3;
                    if (indexTaskBay3 != -1)
                        indexTaskBay3 += 1;
                    if (indexTaskBay3 >= workOrdersCount)
                        indexTaskBay3 = -1;
                }
            }
        }
    }
}