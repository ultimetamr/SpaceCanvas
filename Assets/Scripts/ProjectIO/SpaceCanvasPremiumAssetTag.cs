using UnityEngine;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>
    /// Identifies authored procedural furniture and its saved revision. Keep this
    /// MonoBehaviour in its own matching file so scenes hold a persistent MonoScript.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpaceCanvasPremiumAssetTag : MonoBehaviour
    {
        public const string CurrentRevision = "pbr-finishes-1";
        public string revision;
    }
}
