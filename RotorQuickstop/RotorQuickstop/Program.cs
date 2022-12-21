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
        const string OutputLCDName = "Mining-Display";
        const string BlockTag = "Mining";
        const float pistonsIncrementLength = 0.5f;
        const float rotorVelocity = 0.5f;
        long nextUpdateTick = long.MaxValue;
        bool mining = false;
        bool atMaxDepth = false;
        long finishTime = -1;
        Dictionary<EquipmentType, List<IMyTerminalBlock>> equipmentCache = new Dictionary<EquipmentType, List<IMyTerminalBlock>>();

        enum EquipmentType
        {
            DRILL = 0,
            ROTOR = 1,
            PISTON = 2,
            DISPLAY = 3,
            CARGO = 4
        }

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            PopulateEquipmentCache();
        }

        void PopulateEquipmentCache()
        {
            if (!equipmentCache.ContainsKey(EquipmentType.DRILL)) { equipmentCache.Add(EquipmentType.DRILL, new List<IMyTerminalBlock>()); }
            equipmentCache[EquipmentType.DRILL].Clear();
            equipmentCache[EquipmentType.DRILL] = Drills().Cast<IMyTerminalBlock>().ToList();
            if (!equipmentCache.ContainsKey(EquipmentType.ROTOR)) { equipmentCache.Add(EquipmentType.ROTOR, new List<IMyTerminalBlock>()); }
            equipmentCache[EquipmentType.ROTOR].Clear();
            equipmentCache[EquipmentType.ROTOR] = Rotors().Cast<IMyTerminalBlock>().ToList();
            if (!equipmentCache.ContainsKey(EquipmentType.PISTON)) { equipmentCache.Add(EquipmentType.PISTON, new List<IMyTerminalBlock>()); }
            equipmentCache[EquipmentType.PISTON].Clear();
            equipmentCache[EquipmentType.PISTON] = Pistons().Cast<IMyTerminalBlock>().ToList();
            if (!equipmentCache.ContainsKey(EquipmentType.DISPLAY)) { equipmentCache.Add(EquipmentType.DISPLAY, new List<IMyTerminalBlock>()); }
            equipmentCache[EquipmentType.DISPLAY].Clear();
            equipmentCache[EquipmentType.DISPLAY] = Displays().Cast<IMyTerminalBlock>().ToList();
            if (!equipmentCache.ContainsKey(EquipmentType.CARGO)) { equipmentCache.Add(EquipmentType.CARGO, new List<IMyTerminalBlock>()); }
            equipmentCache[EquipmentType.CARGO].Clear();
            equipmentCache[EquipmentType.CARGO] = Cargo().Cast<IMyTerminalBlock>().ToList();
            foreach (var display in Displays())
            {
                display.Enabled = true;
                display.ContentType = ContentType.TEXT_AND_IMAGE;
                display.Font = "BuildInfoHighlight";
                display.FontSize = 0.5f;
                display.WriteText($"Found {equipmentCache[EquipmentType.DRILL].Count} drills\nFound {equipmentCache[EquipmentType.ROTOR].Count} rotors\nFound {equipmentCache[EquipmentType.PISTON].Count} pistons\nFound {equipmentCache[EquipmentType.CARGO].Count} cargo containers\nFound {equipmentCache[EquipmentType.DISPLAY].Count} displays", false);
            }
        }

        void Log(string msg)
        {
            Echo(msg);
            foreach (var display in Displays())
            {
                display.WriteText(msg + Environment.NewLine);
            }
        }

        void ClearDisplays()
        {
            foreach (var display in Displays())
            {
                display.WriteText("", false);
            }
        }

        public void Main(string arg)
        {
            ParseArguments(arg);
            if (mining)
            {
                if(finishTime != -1)
                {
                    if(DateTime.Now.Ticks > finishTime)
                    {
                        StopMining();
                        return;
                    }
                }
                if (DateTime.Now.Ticks > nextUpdateTick)
                {
                    Log("Next update tick!");
                    if (!atMaxDepth)
                    {
                        PistonExtendUpdate();
                    }
                    nextUpdateTick = DateTime.Now.Ticks + TimeSpan.FromSeconds(((1 / rotorVelocity) / 2) * 60).Ticks;
                    if (AllCargoFull())
                    {
                        Log("Mining stopped due to cargo full");
                        StopMining();
                    }
                }
            }
            else
            {
                var pistons = Pistons();
                if(pistons.Count() > 0)
                {
                    if (pistons.Max(p => p.CurrentPosition) > 0)
                    {
                        if (pistons.All(p => p.CurrentPosition == p.LowestPosition))
                        {
                            foreach (var piston in pistons)
                            {
                                piston.Enabled = false;
                                piston.Velocity = -0.1f;
                                piston.MaxLimit = 0.0f;
                            }
                            atMaxDepth = false;
                        }
                    }
                }
            }
        }

        bool AllCargoFull()
        {
            return Cargo().All(p => p.GetInventory().IsFull);
        }

        void ParseArguments(string arg)
        {
            if (!string.IsNullOrEmpty(arg))
            {
                if (arg == "stop")
                {
                    StopMining();
                }
                else if (arg == "start")
                {
                    if(Pistons().All(p => p.CurrentPosition == p.LowestPosition))
                    {
                        StartMining();
                    }
                    else
                    {
                        Log("Please wait for pistons to fully retract.");
                    }
                }
                else if(arg == "reset-cache")
                {
                    PopulateEquipmentCache();
                }
            }
        }

        #region Wrappers around (rotors, drills, pistons, displays and cargo's), also caches their references.
        List<IMyMotorAdvancedStator> Rotors()
        {
            if (equipmentCache[EquipmentType.ROTOR].Count() > 0)
            {
                return equipmentCache[EquipmentType.ROTOR].Cast<IMyMotorAdvancedStator>().ToList();
            }
            else
            {
                List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
                GridTerminalSystem.GetBlocksOfType(blocks, (IMyTerminalBlock block) => block as IMyMotorAdvancedStator != null && block.CustomName.Contains($"[{BlockTag}]"));
                return blocks.Cast<IMyMotorAdvancedStator>().ToList();
            }
        }

        List<IMyShipDrill> Drills()
        {
            if (equipmentCache[EquipmentType.DRILL].Count() > 0)
            {
                return equipmentCache[EquipmentType.DRILL].Cast<IMyShipDrill>().ToList();
            }
            else
            {
                List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
                GridTerminalSystem.GetBlocksOfType(blocks, (IMyTerminalBlock block) => block as IMyShipDrill != null && block.CustomName.Contains($"[{BlockTag}]"));
                return blocks.Cast<IMyShipDrill>().ToList();
            }
        }

        List<IMyPistonBase> Pistons()
        {
            if(equipmentCache[EquipmentType.PISTON].Count() > 0)
            {
                return equipmentCache[EquipmentType.PISTON].Cast<IMyPistonBase>().ToList();
            }
            else
            {
                List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
                GridTerminalSystem.GetBlocksOfType(blocks, (IMyTerminalBlock block) => block as IMyPistonBase != null && block.CustomName.Contains($"[{BlockTag}]"));
                return blocks.Cast<IMyPistonBase>().ToList();
            }
        }

        List<IMyCargoContainer> Cargo()
        {
            if (equipmentCache[EquipmentType.CARGO].Count() > 0)
            {
                return equipmentCache[EquipmentType.CARGO].Cast<IMyCargoContainer>().ToList();
            }
            else
            {
                List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
                GridTerminalSystem.GetBlocksOfType(blocks, (IMyTerminalBlock block) => block as IMyCargoContainer != null && block.CustomName.Contains($"[{BlockTag}]"));
                return blocks.Cast<IMyCargoContainer>().ToList();
            }
        }

        List<IMyTextPanel> Displays()
        {
            if (equipmentCache[EquipmentType.DISPLAY].Count() > 0)
            {
                return equipmentCache[EquipmentType.DISPLAY].Cast<IMyTextPanel>().ToList();
            }
            else
            {
                List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
                GridTerminalSystem.GetBlocksOfType(blocks, (IMyTerminalBlock block) => block as IMyTextPanel != null && block.CustomName.Contains($"[{OutputLCDName}]"));
                return blocks.Cast<IMyTextPanel>().ToList();
            }
        }
        #endregion

        #region Mining procedures
        void StopMining()
        {
            Log("Stopping mining procedure");
            finishTime = -1;
            mining = false;
            StopRotors();
            StopPistons();
            StopDrills();
        }

        void StopRotors()
        {
            foreach(var rotor in Rotors())
            {
                rotor.Enabled = false;
                rotor.TargetVelocityRPM = 0;
            }
        }

        void StopPistons()
        {
            foreach (var piston in Pistons())
            {
                piston.Velocity = -0.1f;
            }
        }

        void StopDrills()
        {
            foreach (var drill in Drills())
            {
                drill.Enabled = false;
            }
        }

        void StartMining()
        {
            Log("Starting mining procedure");
            mining = true;
            nextUpdateTick = DateTime.Now.Ticks + TimeSpan.FromSeconds(((1 / rotorVelocity) / 2) * 60).Ticks;
            StartRotors();
            StartPistons();
            StartDrills();
        }

        void StartRotors()
        {
            foreach (var rotor in Rotors())
            {
                rotor.Enabled = true;
                rotor.TargetVelocityRPM = rotorVelocity;
            }
        }

        void StartPistons()
        {
            foreach (var piston in Pistons())
            {
                piston.Enabled = true;
                piston.Velocity = 0.1f;
                piston.MaxLimit = pistonsIncrementLength;
            }
        }

        void StartDrills()
        {
            foreach (var drill in Drills())
            {
                drill.Enabled = true;
            }
        }

        void PistonExtendUpdate()
        {
            if(Pistons().Any(p => p.CurrentPosition + pistonsIncrementLength > p.HighestPosition))
            {
                if(Pistons().All(p => p.CurrentPosition == p.HighestPosition))
                {
                    atMaxDepth = true;
                    finishTime = DateTime.Now.Ticks + TimeSpan.FromSeconds(((1 / rotorVelocity) / 2) * 60).Ticks;
                    Log("Pistons are at max depth, setting finish time.");
                }
                else
                {
                    foreach (var piston in Pistons())
                    {
                        piston.MaxLimit = piston.HighestPosition;
                    }
                    Log("Pistons extended to max depth");
                }
            }
            else
            {
                foreach (var piston in Pistons())
                {
                    piston.MaxLimit += pistonsIncrementLength;
                }
                Log("Pistons extended");
            }
        }
        #endregion
    }
}
