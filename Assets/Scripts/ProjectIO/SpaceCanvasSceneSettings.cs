using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceCanvas.ProjectIO
{
    public sealed class SpaceCanvasSceneSettings : MonoBehaviour
    {
        public SceneUnitSystem unitSystem = SceneUnitSystem.Meter;
        public bool gridEnabled = true;
        public float gridSize = 0.25f;

        public SceneSettings ToData()
        {
            return new SceneSettings { unitSystem = unitSystem, gridEnabled = gridEnabled, gridSize = gridSize };
        }

        public void Apply(SceneSettings data)
        {
            if (data == null) return;
            data.EnsureDefaults();
            unitSystem = data.unitSystem;
            gridEnabled = data.gridEnabled;
            gridSize = data.gridSize;
        }
    }

}
