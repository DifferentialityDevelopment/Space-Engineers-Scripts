using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.ModAPI;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        const string rotorName = "Mining Rotor";
        const string drillName = "Mining Drill";
        const string pistonName = "Mining Piston";
        const string lcdName = "Mining LCD";
        const string cargoName = "Mining Cargo";
        float rotationVelocity = 0.5f;
        float cargoFullPerc = 0.95f;
        float excavateVelocity => 0.1f / miningEquipment[MiningEquipmentType.PISTON].Count() / 2;
        bool enabled = false;
        bool initialized = false;
        bool initializing = false;
        bool resetting = false;
        bool finishing = false;
        Dictionary<MiningEquipmentType, List<IMyTerminalBlock>> miningEquipment;
        IMyTerminalBlock outputLCD = null;

        const double updatesPerSecond = 10;
        const double maxCycleTime = 1 / updatesPerSecond;
        double currentCycleTime = 0;

        enum MiningEquipmentType
        {
            ROTOR = 0,
            DRILL = 1,
            PISTON = 2,
            CARGO = 3
        }

        void Main(string arg)
        {
            Initialize();

            if(arg != "")
            {
                ResetCheck();
                FinishCheck();
                CargoCheck();
                ParseArguments(arg);
            }
            else
            {

                currentCycleTime += Runtime.TimeSinceLastRun.TotalSeconds;

                if (currentCycleTime < maxCycleTime)
                    return;

                ResetCheck();
                FinishCheck();
                CargoCheck();
                currentCycleTime = 0;
            }
        }

        void Initialize()
        {
            if (initialized) {
                Log("Already initialized");
                return;
            }
            try
            {
                initializing = true;
                InitializeDisplay();
                SearchMiningEquipment();
                if (AllEquipmentFound())
                {
                    Reset(true);
                    Log("Initialized");
                }
                else
                {
                    if (miningEquipment[MiningEquipmentType.DRILL].Count == 0) { Log("No drills were found"); }
                    if (miningEquipment[MiningEquipmentType.PISTON].Count == 0) { Log("No pistons were found"); }
                    if (miningEquipment[MiningEquipmentType.ROTOR].Count == 0) { Log("No rotors were found"); }
                    Log("Initialization failed");
                }
            }
            catch (Exception e)
            {
                Log($"Error occurred during initialization => {e.Message}\n{e.StackTrace}");
            }
        }

        void InitializeDisplay()
        {
            List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocksOfType(blocks, IsOutputLCD);
            outputLCD = blocks.FirstOrDefault();
            if(outputLCD != null)
            {
                ((IMyTextPanel)outputLCD).Enabled = true;
                ((IMyTextPanel)outputLCD).ContentType = ContentType.TEXT_AND_IMAGE;
                ((IMyTextPanel)outputLCD).Font = "BuildInfoHighlight";
                ((IMyTextPanel)outputLCD).FontSize = 0.5f;
                ((IMyTextPanel)outputLCD).WriteText("", false);
            }
        }

        void SearchMiningEquipment()
        {
            List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
            miningEquipment = new Dictionary<MiningEquipmentType, List<IMyTerminalBlock>>();
            miningEquipment.Clear();
            miningEquipment.Add(MiningEquipmentType.ROTOR, new List<IMyTerminalBlock>());
            miningEquipment.Add(MiningEquipmentType.DRILL, new List<IMyTerminalBlock>());
            miningEquipment.Add(MiningEquipmentType.PISTON, new List<IMyTerminalBlock>());
            miningEquipment.Add(MiningEquipmentType.CARGO, new List<IMyTerminalBlock>());
            GridTerminalSystem.SearchBlocksOfName("Mining", blocks, IsMiningEquipment);
            Log($"Found {blocks.Count} mining equipment blocks!");
            foreach (var block in blocks)
            {
                if (IsMiningRotor(block)) { 
                    miningEquipment[MiningEquipmentType.ROTOR].Add(block);
                    Log($"Adding {block.CustomName} to rotors");
                }
                if (IsMiningDrill(block)) { 
                    miningEquipment[MiningEquipmentType.DRILL].Add(block);
                    Log($"Adding {block.CustomName} to drills");
                }
                if (IsMiningPiston(block)) { 
                    miningEquipment[MiningEquipmentType.PISTON].Add(block);
                    Log($"Adding {block.CustomName} to pistons");
                }
                if (IsMiningCargo(block))
                {
                    miningEquipment[MiningEquipmentType.CARGO].Add(block);
                    Log($"Adding {block.CustomName} to cargo");
                }
            }
        }

        bool AllEquipmentFound()
        {
            if(miningEquipment[MiningEquipmentType.CARGO].Count == 0) { return false; }
            if(miningEquipment[MiningEquipmentType.DRILL].Count == 0) { return false; }
            if(miningEquipment[MiningEquipmentType.PISTON].Count == 0) { return false; }
            if(miningEquipment[MiningEquipmentType.ROTOR].Count == 0) { return false; }
            return true;
        }

        public void ParseArguments(string args)
        {
            if(!initialized) { return; }
            try
            {
                if (args == "enable" && !enabled)
                {
                    if (!initialized)
                    {
                        Log("Cannot start mining, not yet initialized!");
                        return;
                    }
                    StartMining();
                }
                else if (args == "disable" && enabled)
                {
                    StopMining();
                }
                else if (args == "reset")
                {
                    Reset(false);
                }
                else if (args.StartsWith("set-speed") && enabled)
                {
                    rotationVelocity = float.Parse(args.Split(' ').Last().Trim());
                    AdjustRotorSpeed(false);
                }
            }
            catch (Exception e)
            {
                Log($"Error occurred during argument parsing => {e.Message}");
            }
        }

        void CargoCheck()
        {
            if(Cargo().All(p => InventoryFull(p.GetInventory())))
            {
                Log("All mining cargo is full, stopping mining.");
                StopMining();
            }
        }

        bool InventoryFull(IMyInventory inv)
        {
            return (double)inv.MaxVolume.RawValue / (double)1 * (double)inv.CurrentVolume.RawValue > (double)cargoFullPerc;
        }

        void FinishCheck()
        {
            if (enabled)
            {
                if (Pistons().All(p => p.CurrentPosition >= p.HighestPosition))
                {
                    finishing = true;
                    Reset();
                }
            }
        }

        void ResetCheck()
        {
            if (resetting)
            {
                var pistons = Pistons();
                if (pistons.All(p => p.CurrentPosition <= p.LowestPosition))
                {
                    Log($"Resetting has finished");
                    resetting = false;
                    if (initializing)
                    {
                        initializing = false;
                        initialized = true;
                    }
                    else if (finishing)
                    {
                        Log("Excavation process has finished!");
                        finishing = false;
                    }
                    foreach (var piston in pistons)
                    {
                        piston.Enabled = false;
                        piston.Velocity = 0f;
                    }
                }
            }
        }

        void Reset(bool skipSearch = false)
        {
            Log($"Resetting");
            if (!skipSearch)
            {
                SearchMiningEquipment();
            }
            StopMining();
        }

        void Log(string message)
        {
            if(outputLCD != null)
            {
                IMyTextPanel panel = outputLCD as IMyTextPanel;
                if(!panel.WriteText(message + Environment.NewLine, true))
                {
                    Echo(message);
                }
            }
            else
            {
                Echo(message);
            }
        }

        void StartMining()
        {
            Log("Starting excavation.");
            enabled = true;
            foreach(var rotor in Rotors())
            {
                rotor.Enabled = true;
            }
            float velocity = excavateVelocity;
            Log($"Piston velocity: {velocity}");
            foreach (var piston in Pistons())
            {
                piston.Enabled = true;
                piston.Velocity = velocity;
            }
            foreach (var drill in Drills())
            {
                drill.Enabled = true;
            }
            AdjustRotorSpeed(false);
            Log("Excavation has started.");
        }

        List<IMyMotorAdvancedStator> Rotors()
        {
            return miningEquipment[MiningEquipmentType.ROTOR].ConvertAll((block) => block as IMyMotorAdvancedStator);
        }

        List<IMyShipDrill> Drills()
        {
            return miningEquipment[MiningEquipmentType.DRILL].ConvertAll((block) => block as IMyShipDrill);
        }

        List<IMyPistonBase> Pistons()
        {
            return miningEquipment[MiningEquipmentType.PISTON].ConvertAll((block) => block as IMyPistonBase);
        }

        List<IMyCargoContainer> Cargo()
        {
            return miningEquipment[MiningEquipmentType.CARGO].ConvertAll((block) => block as IMyCargoContainer);
        }

        void StopMining()
        {
            resetting = true;
            enabled = false;
            foreach (var rotor in Rotors())
            {
                rotor.Enabled = false;
            }
            foreach (var drill in Drills())
            {
                drill.Enabled = false;
            }
            foreach (var piston in Pistons())
            {
                piston.Enabled = true;
                piston.Velocity = -1f;
            }
            AdjustRotorSpeed(true);
            Log("Excavation has finished.");
        }

        void AdjustRotorSpeed(bool stop = false)
        {
            float velocity = stop ? 0f : rotationVelocity;
            foreach (var rotor in Rotors())
            {
                rotor.SetValue<float>("Velocity", velocity);
            }
            Log($"Rotor speed adjusted to {velocity}");
        }

        bool IsMiningEquipment(IMyTerminalBlock block)
        {
            if(block.CustomName.Contains("Mining"))
            {
                return IsMiningRotor(block) || IsMiningDrill(block) || IsMiningPiston(block) || IsMiningCargo(block);
            }
            return false;
        }

        bool IsMiningRotor(IMyTerminalBlock block)
        {
            var cast = block as IMyMotorStator;
            return cast != null && (cast.CustomName.Contains(rotorName));
        }

        bool IsMiningDrill(IMyTerminalBlock block)
        {
            var cast = block as IMyShipDrill;
            return cast != null && (cast.CustomName.Contains(drillName));
        }

        bool IsMiningPiston(IMyTerminalBlock block)
        {
            var cast = block as IMyPistonBase;
            return cast != null && (cast.CustomName.Contains(pistonName));
        }

        bool IsMiningCargo(IMyTerminalBlock block)
        {
            var cast = block as IMyCargoContainer;
            return cast != null && (cast.CustomName.Contains(cargoName));
        }

        bool IsOutputLCD(IMyTerminalBlock block)
        {
            var cast = block as IMyTextPanel;
            return cast != null && block.CustomName.Contains(lcdName);
        }
    }
}
