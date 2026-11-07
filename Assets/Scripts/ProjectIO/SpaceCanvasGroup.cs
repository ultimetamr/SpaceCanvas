using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceCanvas.ProjectIO
{
    public sealed class SpaceCanvasGroup : MonoBehaviour
    {
        public string groupId;
        public string groupName = "Group";
        public List<string> childIds = new List<string>();

        public SceneGroup ToData()
        {
            if (string.IsNullOrWhiteSpace(groupId)) groupId = Guid.NewGuid().ToString("N");
            return new SceneGroup { groupId = groupId, groupName = groupName, childIds = new List<string>(childIds ?? new List<string>()) };
        }

        public void Apply(SceneGroup data)
        {
            if (data == null) return;
            data.EnsureDefaults(0);
            groupId = data.groupId;
            groupName = data.groupName;
            childIds = new List<string>(data.childIds);
        }
    }
}
