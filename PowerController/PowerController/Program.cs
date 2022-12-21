using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        const string OutputLCDName = "PP-Display";
        List<IMyPowerProducer> PowerProducers = new List<IMyPowerProducer>();
        IMyTerminalBlock OutputLCD = null;
        bool Initialized = false;
        long LastUpdateTime;

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update100;
        }

        public void Save()
        {
        }

        void InitializeDisplay()
        {
            List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocksOfType(blocks, IsOutputLCD);
            OutputLCD = blocks.FirstOrDefault();
            if (OutputLCD != null)
            {
                ((IMyTextPanel)OutputLCD).Enabled = true;
                ((IMyTextPanel)OutputLCD).ContentType = ContentType.TEXT_AND_IMAGE;
                ((IMyTextPanel)OutputLCD).Font = "BuildInfoHighlight";
                ((IMyTextPanel)OutputLCD).FontSize = 0.5f;
                ((IMyTextPanel)OutputLCD).WriteText("", false);
            }
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if (!Initialized)
            {
                GridTerminalSystem.GetBlocksOfType(PowerProducers, IsPowerProducer);
                InitializeDisplay();
                LastUpdateTime = DateTime.Now.Ticks;
                Initialized = true;
            }
            try
            {
                if (OutputLCD != null && PowerProducers.Count > 0 && LastUpdateTime + TimeSpan.FromSeconds(1).Ticks < DateTime.Now.Ticks)
                {
                    UpdatePowerStats();
                    LastUpdateTime = DateTime.Now.Ticks;
                }
            }
            catch (Exception e)
            {
                IMyTextPanel panel = OutputLCD as IMyTextPanel;
                OutputToLCD(ref panel, $"{e.Message}\n{e.StackTrace}");
            }
        }

        void UpdatePowerStats()
        {
            IMyTextPanel panel = OutputLCD as IMyTextPanel;
            OutputToLCD(ref panel, $"Power Stats - {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}", false);
            float combinedMwh = 0;
            foreach(var pp in PowerProducers)
            {
                if (pp.DetailedInfo.Contains("Stored power:"))
                {
                    string storedPower = GetDetailedInfoProperty(pp, "Stored power:");
                    string inputPower = GetDetailedInfoProperty(pp, "Current Input:");
                    string outputPower = GetDetailedInfoProperty(pp, "Current Output:");
                    OutputToLCD(ref panel, $"[{pp.CustomName}] Stored: {storedPower}, Input: {inputPower}, Output: {outputPower}");
                    //combinedMwh += pp.CurrentOutput;
                }
                else
                {
                    OutputToLCD(ref panel, $"{pp.CustomName}: {pp.CurrentOutput * 1000} KWh, Max: {pp.MaxOutput * 1000} KWh");
                    combinedMwh += pp.CurrentOutput;
                }
            }
            OutputToLCD(ref panel, $"Total Input: {combinedMwh * 1000} KWh");
        }
        
        string GetDetailedInfoProperty(IMyTerminalBlock block, string propertyName)
        {
            try
            {
                string detailedInfo = block.DetailedInfo;
                string[] detailedInfoSplit = detailedInfo.Split('\n');
                string[] infoLineSplit = detailedInfoSplit.First(p => p.StartsWith(propertyName)).Split(' ');
                return $"{infoLineSplit[infoLineSplit.Length - 2]} {infoLineSplit[infoLineSplit.Length - 1]}";
            }
            catch(Exception e)
            {
                IMyTextPanel panel = OutputLCD as IMyTextPanel;
                OutputToLCD(ref panel, $"Unknown Property: {propertyName}");
                return string.Empty;
            }
        }

        void OutputToLCD(ref IMyTextPanel panel, string message, bool append = true)
        {
            if (!panel.WriteText(message + Environment.NewLine, append))
            {
                Echo(message);
            }
        }

        bool IsPowerProducer(IMyTerminalBlock block)
        {
            var cast = block as IMyPowerProducer;
            return cast != null;
        }

        bool IsOutputLCD(IMyTerminalBlock block)
        {
            var cast = block as IMyTextPanel;
            return cast != null && block.CustomName.Contains($"[{OutputLCDName}]");
        }
    }
}
