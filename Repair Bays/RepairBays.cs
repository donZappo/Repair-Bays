using System;
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

            bool auto1 = false;
            bool auto2 = false;

            if (instance.shipUpgrades.Any(u => u.Tags.Any(t => t.Contains("argo_mechBay_automation1"))))
                auto1 = true;

            if (instance.shipUpgrades.Any(u => u.Tags.Any(t => t.Contains("argo_mechBay_automation2"))))
                auto2 = true;

            if (instance.MechLabQueue.Count < 1 || !passDay) return;
            int numMechBayPods = instance.CompanyStats.GetValue<int>(__instance.Constants.Story.MechBayPodsID);
            if (instance.MechLabQueue.Count() >= 3 && numMechBayPods >= 3)
            {
                float corfact = 0.3333333f;
                if (auto2)
                    corfact = 1;
                instance.MechLabQueue[2].PayCost(Convert.ToInt32(corfact * instance.MechTechSkill));
            }

            if (instance.MechLabQueue.Count() >= 2 && numMechBayPods >= 2)
            {
                float corfact = 0.5f;
                if (auto1)
                    corfact = 1;
                instance.MechLabQueue[1].PayCost(Convert.ToInt32(corfact * instance.MechTechSkill));
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
            int numMechBayPods = ___Sim.CompanyStats.GetValue<int>(___Sim.Constants.Story.MechBayPodsID);

            bool auto1 = false;
            bool auto2 = false;

            if (___Sim.shipUpgrades.Any(u => u.Tags.Any(t => t.Contains("argo_mechBay_automation1"))))
                auto1 = true;

            if (___Sim.shipUpgrades.Any(u => u.Tags.Any(t => t.Contains("argo_mechBay_automation2"))))
                auto2 = true;

            int cumulativeDays = 0;

            for (int i = 0; i < ___Sim.MechLabQueue.Count; i++)
            {
                WorkOrderEntry workOrderEntry2 = ___Sim.MechLabQueue[i];
                TaskManagementElement taskManagementElement = null;
                if (__instance.ActiveItems.TryGetValue(workOrderEntry2, out taskManagementElement) && i == 0)
                {
                    if (___Sim.WorkOrderIsMechTech(workOrderEntry2.Type))
                    {
                        cumulativeDays = taskManagementElement.UpdateItem(cumulativeDays);
                    }
                    else
                    {
                        taskManagementElement.UpdateItem(0);
                    }
                }

                if (__instance.ActiveItems.TryGetValue(workOrderEntry2, out taskManagementElement) && i == 1)
                {
                    if (___Sim.WorkOrderIsMechTech(workOrderEntry2.Type) && numMechBayPods == 1)
                    {
                        cumulativeDays = taskManagementElement.UpdateItem(cumulativeDays);
                    }
                    else if (___Sim.WorkOrderIsMechTech(workOrderEntry2.Type) && numMechBayPods > 1)
                    {
                        int temptime = taskManagementElement.UpdateItem(0);
                        if (auto1)
                            temptime = 0;
                        taskManagementElement.UpdateItem(Convert.ToInt32(temptime));
                    }
                    else
                    {
                        taskManagementElement.UpdateItem(0);
                    }
                }

                if (__instance.ActiveItems.TryGetValue(workOrderEntry2, out taskManagementElement) && i == 2)
                {
                    if (___Sim.WorkOrderIsMechTech(workOrderEntry2.Type) && numMechBayPods <= 2)
                    {
                        cumulativeDays = taskManagementElement.UpdateItem(cumulativeDays);
                    }
                    else if (___Sim.WorkOrderIsMechTech(workOrderEntry2.Type) && numMechBayPods > 2)
                    {
                        int temptime = taskManagementElement.UpdateItem(0);
                        if (auto2)
                            temptime = 0;
                        taskManagementElement.UpdateItem(Convert.ToInt32(2 * temptime));
                    }
                    else
                    {
                        taskManagementElement.UpdateItem(0);
                    }
                }

                if (__instance.ActiveItems.TryGetValue(workOrderEntry2, out taskManagementElement) && i > 2)
                {
                    if (___Sim.WorkOrderIsMechTech(workOrderEntry2.Type))
                    {
                        cumulativeDays = taskManagementElement.UpdateItem(cumulativeDays);
                    }
                    else
                    {
                        taskManagementElement.UpdateItem(0);
                    }
                }
            }
        }
    }
}