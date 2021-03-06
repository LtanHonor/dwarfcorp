﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DwarfCorp
{
    public partial class VoxelHelpers
    {
        public static bool DoesVoxelHaveVisibleSurface(ChunkData Data, VoxelHandle V)
        {
            if (!V.IsValid)
                return false;

            if (!V.IsVisible || V.IsEmpty) return false;

            foreach (var neighborCoordinate in VoxelHelpers.EnumerateManhattanNeighbors(V.Coordinate))
            {
                var neighbor = new VoxelHandle(Data, neighborCoordinate);
                if (!neighbor.IsValid) return true;
                if (neighbor.IsEmpty && neighbor.IsExplored) return true;
                if (!neighbor.IsVisible) return true;
            }

            return false;
        }
    }
}
