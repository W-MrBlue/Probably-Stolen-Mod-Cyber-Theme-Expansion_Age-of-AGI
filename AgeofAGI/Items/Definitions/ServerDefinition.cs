using System;

namespace AgeOfAgi.Items.Definitions
{
    internal sealed class ServerDefinition
    {
        internal string ItemId { get; }
        internal string TrainedOutputId { get; }
        internal float CoolingMultiplier { get; }
        internal int CpuUpgradeUnitsStep1 { get; }
        internal int CpuUpgradeUnitsStep2 { get; }
        internal int CpuUpgradeUnitsTotal => CpuUpgradeUnitsStep1 + CpuUpgradeUnitsStep2;
        internal int GpuUpgradeUnitsPerStep { get; }
        internal int MemoryUpgradeUnits { get; }
        internal float IntelligenceWork { get; }
        internal float BioBaseWork { get; }
        internal float BioComplexityWork { get; }
        internal int DataUnitsPer8Gb { get; }
        internal float DataWorkPerUnit { get; }

        internal ServerDefinition(string itemId, string trainedOutputId, float coolingMultiplier,
            int cpuUpgradeUnitsStep1, int cpuUpgradeUnitsStep2, int gpuUpgradeUnitsPerStep,
            float intelligenceWork = 120,
            float bioBaseWork = 40, float bioComplexityWork = 20,
            int dataUnitsPer8Gb = 8, float dataWorkPerUnit = 1,
            int memoryUpgradeUnits = 8)
        {
            DefinitionValidation.RequireItemId(itemId, nameof(itemId), "Server");
            DefinitionValidation.RequireItemId(trainedOutputId, nameof(trainedOutputId), "Trained AI");
            DefinitionValidation.RequireFinitePositive(coolingMultiplier, nameof(coolingMultiplier), "coolingMultiplier");
            if (coolingMultiplier > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(coolingMultiplier), "Cooling multiplier must not exceed one.");
            }
            DefinitionValidation.RequirePositive(cpuUpgradeUnitsStep1, nameof(cpuUpgradeUnitsStep1), "cpuUpgradeUnitsStep1");
            DefinitionValidation.RequirePositive(cpuUpgradeUnitsStep2, nameof(cpuUpgradeUnitsStep2), "cpuUpgradeUnitsStep2");
            if ((long)cpuUpgradeUnitsStep1 + cpuUpgradeUnitsStep2 > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(cpuUpgradeUnitsStep2), "CPU expansion stages must fit in an Int32 material counter.");
            DefinitionValidation.RequirePositive(gpuUpgradeUnitsPerStep, nameof(gpuUpgradeUnitsPerStep), "gpuUpgradeUnitsPerStep");
            if (gpuUpgradeUnitsPerStep > int.MaxValue / 2)
            {
                throw new ArgumentOutOfRangeException(nameof(gpuUpgradeUnitsPerStep), "Two GPU stages must fit in an Int32 material counter.");
            }
            ItemId = itemId;
            TrainedOutputId = trainedOutputId;
            CoolingMultiplier = coolingMultiplier;
            CpuUpgradeUnitsStep1 = cpuUpgradeUnitsStep1;
            CpuUpgradeUnitsStep2 = cpuUpgradeUnitsStep2;
            GpuUpgradeUnitsPerStep = gpuUpgradeUnitsPerStep;
            DefinitionValidation.RequirePositive(memoryUpgradeUnits, nameof(memoryUpgradeUnits), "memoryUpgradeUnits");
            MemoryUpgradeUnits = memoryUpgradeUnits;
            DefinitionValidation.RequireFinitePositive(intelligenceWork, nameof(intelligenceWork), "intelligenceWork");
            DefinitionValidation.RequireFinitePositive(bioBaseWork, nameof(bioBaseWork), "bioBaseWork");
            DefinitionValidation.RequireFinitePositive(bioComplexityWork, nameof(bioComplexityWork), "bioComplexityWork");
            IntelligenceWork = intelligenceWork;
            BioBaseWork = bioBaseWork;
            BioComplexityWork = bioComplexityWork;
            DefinitionValidation.RequirePositive(dataUnitsPer8Gb, nameof(dataUnitsPer8Gb), "dataUnitsPer8Gb");
            DefinitionValidation.RequireFinitePositive(dataWorkPerUnit, nameof(dataWorkPerUnit), "dataWorkPerUnit");
            DataUnitsPer8Gb = dataUnitsPer8Gb;
            DataWorkPerUnit = dataWorkPerUnit;
        }
    }
}
