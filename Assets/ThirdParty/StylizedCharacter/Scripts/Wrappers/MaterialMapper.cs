using System;
using System.Collections.Generic;
using UnityEngine;

namespace NHance.Assets.Scripts.Items
{
    [Serializable]
    public class MaterialMapper
    {
        public bool isOpened = false;
        public List<Material> Materials = new List<Material>();
        public List<BodypartWrapper> Wrappers = new List<BodypartWrapper>();
    }
}