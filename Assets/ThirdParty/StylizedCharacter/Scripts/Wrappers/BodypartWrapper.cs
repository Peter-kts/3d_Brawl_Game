using System;
using System.Collections.Generic;
using NHance.Assets.Scripts.Enums;
using UnityEngine;

namespace NHance.Assets.Scripts
{
    [Serializable]
    public class BodypartWrapper
    {
        public TargetBodyparts Type;
        public bool Enabled;
        public GameObject Target;

        [NonSerialized] private SkinnedMeshRenderer _renderer;

        public SkinnedMeshRenderer Renderer
        {
            get
            {
                if (Target != null)
                {
                    if(_renderer == null)
                        _renderer = Target.GetComponentInChildren<SkinnedMeshRenderer>();
                    return _renderer;
                }
                return null;
            }
        }

        public BodypartWrapper(TargetBodyparts type, GameObject target)
        {
            Type = type;
            Target = target;
        }

        public BodypartWrapper(TargetBodyparts type, bool enabled)
        {
            Type = type;
            Enabled = enabled;
        }

        public void Clean()
        {
            _renderer = null;
        }
    }
}