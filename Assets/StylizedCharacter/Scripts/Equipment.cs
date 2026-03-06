using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NHance.Assets.Scripts.Items;
using UnityEngine;

namespace NHance.Assets.Scripts
{
    public class Equipment : MonoBehaviour
    {
        public Transform Target;
        
        void Start()
        {
            var boneMap = new Dictionary<string, Transform>();
            GetAllSkinnedMeshRenderers(ref boneMap, Target);
            List<SkinnedMeshRenderer> renderList = GetComponents<SkinnedMeshRenderer>().ToList();
            renderList.AddRange(GetComponentsInChildren<SkinnedMeshRenderer>());
            
            //nothing to map
            if (renderList.Count == 0)
                return;

            foreach (var srenderer in renderList)
            {
                Transform[] newBones = new Transform[srenderer.bones.Length];

                for (int i = 0; i < srenderer.bones.Length; ++i)
                {
                    GameObject bone = srenderer.bones[i].gameObject;

                    if (!boneMap.TryGetValue(bone.name, out newBones[i]))
                    {
                        Debug.LogWarning("Unable to map bone \"" + bone.name + "\" to target skeleton.");
                    }
                }
                srenderer.bones = newBones;
                srenderer.rootBone = FindBoundByName(srenderer.rootBone.name, boneMap);
                srenderer.updateWhenOffscreen = true;
            }
        }

        void GetAllSkinnedMeshRenderers(ref Dictionary<string, Transform> map, Transform target)
        {
            if(!map.ContainsKey(target.name))
                map.Add(target.name, target);
            foreach (Transform child in target)
            {
                if(child.gameObject.GetComponent<NHItem>() == null)
                    GetAllSkinnedMeshRenderers(ref map, child);
            }
        }

        private Transform FindBoundByName(string _name, Dictionary<string, Transform> boneMap)
        {
            Transform _rootBone;

            if (!boneMap.TryGetValue(_name, out _rootBone))
            {
                Debug.LogWarning("Unable to map bone \"" + _name + "\" to target skeleton.");
            }
            return _rootBone;
        }
    }
}