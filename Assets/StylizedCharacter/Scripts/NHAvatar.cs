using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NHance.Assets.Scripts.Attributes;
using NHance.Assets.Scripts.Enums;
using NHance.Assets.Scripts.Items;
using NHance.Assets.Scripts.Utils;
using NHance.Assets.StylizedCharacter.Scripts;
using NHance.Assets.StylizedCharacter.Scripts.Wrappers;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NHance.Assets.Scripts
{
    [HelpURL("https://github.com/DustCoron/NhanceRPGDocumentation/wiki/Avatar")]
    public partial class NHAvatar : MonoBehaviour
    {
        public ItemWrapperHandler Items = new ItemWrapperHandler();
        public GameObjectByItemTypeWrapper Cache = new GameObjectByItemTypeWrapper();
        public GameObjectByBoneTypeWrapper SocketMap = new GameObjectByBoneTypeWrapper();
        public ItemTypeToBoneTypeWrapper itemToBoneMapper = new ItemTypeToBoneTypeWrapper();
        public Transform rootBone;
        public Transform rootGeometry;

        public List<BodypartWrapper> PartsMap = new List<BodypartWrapper>();

        private Animator _animator;
        private int _animationIndex;
        private List<AnimationClip> _animationClips;

        public string characterPrefix;
        public bool FoldoutBodyPartSetup = false;
        public bool FoldoutSocketsSetup = false;
        public bool FoldoutSocketsTransformSetup = false;
        public bool FoldoutItemToBoneMapper = false;
        public bool showAnimationControls = false;
        public Gender Gender;

        public List<Material> BodyMaterialsToReplace = new List<Material>();
        public List<Material> HeadMaterialsToReplace = new List<Material>();
        public List<Material> DefaultBodyMaterial = new List<Material>();
        public List<Material> DefaultHeadMaterial = new List<Material>();
        public List<Material> DefaultFemBrowsMaterial = new List<Material>();
        public List<Material> DefaultOrcFemBrowsMaterial = new List<Material>();
        public List<Material> DefaultEyesMaterial = new List<Material>();
        public bool Auto;

        public Material[] HeadMaterial =>
            Items[ItemTypeEnum.HeadSkin] != null && Items[ItemTypeEnum.HeadSkin].Mappers.SelectMany(m => m.Materials)
                .Where(m => m != null).ToList().Count > 0
                ? Items[ItemTypeEnum.HeadSkin].Mappers.SelectMany(m => m.Materials).Where(m => m != null).ToArray()
                : DefaultHeadMaterial.Where(m => m != null).ToArray();

        public Material[] EyeMaterial =>
            Items[ItemTypeEnum.Eyes] != null && Items[ItemTypeEnum.Eyes].Mappers.SelectMany(m => m.Materials)
                .Where(m => m != null).ToList().Count > 0
                ? Items[ItemTypeEnum.Eyes].Mappers.SelectMany(m => m.Materials).Where(m => m != null).ToArray()
                : DefaultEyesMaterial.Where(m => m != null).ToArray();

        public Material[] FemBrowsMaterial =>
            Items[ItemTypeEnum.FemBrows] != null && Items[ItemTypeEnum.FemBrows].Mappers.SelectMany(m => m.Materials)
                .Where(m => m != null).ToList().Count > 0
                ? Items[ItemTypeEnum.FemBrows].Mappers.SelectMany(m => m.Materials).Where(m => m != null).ToArray()
                : DefaultFemBrowsMaterial.Where(m => m != null).ToArray();
        public Material[] OrcBrowsMaterial =>
        Items[ItemTypeEnum.OrF_Brows] != null && Items[ItemTypeEnum.OrF_Brows].Mappers.SelectMany(m => m.Materials)
        .Where(m => m != null).ToList().Count > 0
        ? Items[ItemTypeEnum.OrF_Brows].Mappers.SelectMany(m => m.Materials).Where(m => m != null).ToArray()
        : DefaultOrcFemBrowsMaterial.Where(m => m != null).ToArray();

        private void Start()
        {
            if(_animator == null)
                _animator = GetComponent<Animator>();
            if(_animator == null)
                _animator = GetComponentInChildren<Animator>();
            _animationClips = _animator.runtimeAnimatorController.animationClips.ToList();
            NormalizeAnimationIndex();
        }

        public NHAvatar SetItem(NHItem item)
        {
            if (item == null) return this;

            Items[item.Type] = item;
            CleanCache(item.Type);
            if (item.Type == ItemTypeEnum.WeaponL)
                ClearAnotherType(item, ItemTypeEnum.WeaponR);
            if (item.Type == ItemTypeEnum.WeaponR)
                ClearAnotherType(item, ItemTypeEnum.WeaponL);
            if (item.Type == ItemTypeEnum.Hair)
                ClearAnotherType(ItemTypeEnum.Helmet);
            if (item.Type == ItemTypeEnum.Helmet)
                ClearAnotherType(ItemTypeEnum.Hair);
            return this;
        }

        public NHAvatar SetItems(params NHItem[] items)
        {
            foreach (var item in items)
                SetItem(item);
            return this;
        }

        public NHAvatar ClearItems(params ItemTypeEnum[] types)
        {
            if (types.Length == 0)
            {
                Items.Clear();
                Cache.Clear();
            }
            else
            {
                foreach (var type in types)
                {
                    Items.Remove(type);
                    CleanCache(type);
                }
            }

            return this;
        }

        private void ClearAnotherType(NHItem item, ItemTypeEnum anotherType)
        {
            if (item != null && item.IsTakeTwoHands)
            {
                CleanCache(anotherType);
                Items.Remove(anotherType);
            }
        }

        private void ClearAnotherType(ItemTypeEnum anotherType)
        {
            CleanCache(anotherType);
            Items.Remove(anotherType);
        }

        public NHAvatar Compile()
        {
            InitSkinMaterials();

            foreach (var item in Items._list)
            {
                InitComponent(item.Item);
            }

            return this;
        }

        private void InitSkinMaterials()
        {
            foreach (var part in PartsMap.Where(m => m.Target != null))
            {
                Material[] m = GetMaterial(part.Type);
                if (m != null)
                    part.Renderer.sharedMaterials = m;
            }
        }

        private Material[] GetMaterial(TargetBodyparts partType)
        {
            switch (partType)
            {
                case TargetBodyparts.Head:
                case TargetBodyparts.Tusks:
                    return HeadMaterial;
                case TargetBodyparts.Ears:
                    return HeadMaterial;
                case TargetBodyparts.Eyes:
                    return EyeMaterial;
                case TargetBodyparts.FemBrows:
                    return FemBrowsMaterial;
                case TargetBodyparts.OrcFemaleBrows:
                    return OrcBrowsMaterial;
                case TargetBodyparts.Torso:
                case TargetBodyparts.Bracers:
                case TargetBodyparts.Hands:
                case TargetBodyparts.Pants:
                case TargetBodyparts.Boots:
                case TargetBodyparts.Feet:
                    return GetBodyMaterials().ToArray();
            }

            return null;
        }
		private Vector3 SocketPosition(NHItem SocketItem)
		{
            if (SocketItem == null)
                return  Vector3.zero;

            var s = SocketMap.Get(itemToBoneMapper[SocketItem.Type]);
            return s == null ? Vector3.zero : s.Setup.Position;
		}

        private Quaternion SocketRotation(NHItem SocketItem)
        {
            if (SocketItem == null)
                return Quaternion.Euler(Vector3.zero);

            
            var s = SocketMap.Get(itemToBoneMapper[SocketItem.Type]);
            return s == null ? Quaternion.Euler(Vector3.zero) : Quaternion.Euler(SocketMap.Get(itemToBoneMapper[SocketItem.Type]).Setup.Rotation);
        }

        private void InitComponent(NHItem obj)
        {
            if (obj == null)
                return;

            switch (obj.Type)
            {
                case ItemTypeEnum.Skin:
                case ItemTypeEnum.HeadSkin:
                case ItemTypeEnum.Eyes:
                case ItemTypeEnum.FemBrows:
                    return;
            }

            var temp = GetTargetBodyparts(obj);
            foreach (var e in PartsMap.Where(p => p.Target != null))
                if (temp.Contains(e.Type))
                    ActivateBodyPart(e.Target, false);


            Transform parent = SocketMap[itemToBoneMapper[obj.Type]] == null ? transform : SocketMap[itemToBoneMapper[obj.Type]].transform;
			
            Vector3 position = SocketPosition(obj);
            Quaternion rotation = SocketRotation(obj);
 
            Object itemInstance;
#if UNITY_EDITOR
            if (Application.isPlaying)
                itemInstance = Instantiate(obj.gameObject);
            else
                itemInstance = PrefabUtility.InstantiatePrefab(obj.gameObject);
            
#else
            itemInstance = Instantiate(obj.gameObject);
#endif

            var result = (GameObject) itemInstance;
            result.transform.SetParent(parent);
            result.transform.localPosition = position;
            result.transform.localRotation = rotation;
            Cache[obj.Type] = result;

            var bodyMaterials = GetBodyMaterials();

            if (bodyMaterials.Count > 0 && obj.CopySkinMaterial)
            {
                foreach (var mr in result.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    bool haveSkinMatToReplace = false;
                    foreach (var bmat in BodyMaterialsToReplace)
                        if (mr.sharedMaterials.Count(mt => mt.name == bmat.name) > 0)
                            haveSkinMatToReplace = true;
                    
                    bool haveHeadMatToReplace = false;
                    foreach (var hmat in HeadMaterialsToReplace)
                        if (mr.sharedMaterials.Count(mt => mt.name == hmat.name) > 0)
                            haveHeadMatToReplace = true;
                    
                    
                    if (haveSkinMatToReplace)
                    {
                        List<Material> m = new List<Material>();
                        m.AddRange(bodyMaterials);
                        if (BodyMaterialsToReplace != null)
                            m.AddRange(mr.sharedMaterials.ToList().Where(mt => !BodyMaterialsToReplace.Select(mtd => mtd.name).Contains(mt.name)));
                        mr.sharedMaterials = m.Where(mtd => m != null).ToArray();
                    }

                    if (haveHeadMatToReplace)
                    {
                        List<Material> m = new List<Material>();
                        m.AddRange(HeadMaterial);
                        if (HeadMaterialsToReplace != null)
                            m.AddRange(mr.sharedMaterials.ToList().Where(mt => !HeadMaterialsToReplace.Select(mtd => mtd.name).Contains(mt.name)));
                        mr.sharedMaterials = m.Where(mtd => mtd != null).ToArray();
                    }
                }
            }

            var parts = PartsMap.Where(p => p.Target != null);

            if (obj.Mappers.Count > 0)
            {
                foreach (var mapper in obj.Mappers)
                {
                    if (mapper.Wrappers.Count(w => w.Enabled) == 0)
                        continue;

                    var mappersTypes = mapper.Wrappers.Where(w => w.Enabled).Select(w => w.Type);


                    foreach (var e in parts)
                        if (mappersTypes.Contains(e.Type))
                        {
                            var mats = e.Renderer.sharedMaterials.ToList();
                            mats.AddRange(mapper.Materials);
                            e.Renderer.sharedMaterials = mats.Where(m => m != null).ToArray();
                        }
                }
            }

            if (SocketMap[itemToBoneMapper[obj.Type]] == null)
                result.AddComponent<Equipment>().Target = rootBone;
        }

        private List<Material> GetBodyMaterials()
        {
            var result = new List<Material>();
            var item = Items[ItemTypeEnum.Skin];
            result.AddRange(item != null
                ? item.Mappers.SelectMany(m => m.Materials).Where(m => m != null).ToList()
                : DefaultBodyMaterial.Where(m => m != null));

            List<ItemTypeEnum> skins = new List<ItemTypeEnum>()
            {
                ItemTypeEnum.BodyAdditional,
                ItemTypeEnum.UnderwearSkin,
                ItemTypeEnum.ChestSkin,
                ItemTypeEnum.GlovesSkin,
                ItemTypeEnum.PantsSkin
            };

            foreach (var type in skins)
            {
                item = Items[type];
                if (item != null)
                    result.AddRange(item.Mappers.SelectMany(m => m.Materials).Where(m => m != null));
            }

            return result;
        }

        public void ActivateBodyPart(GameObject bodypart, bool enabled)
        {
            SkinnedMeshRenderer r = bodypart.GetComponent<SkinnedMeshRenderer>();
            if (r == null)
                bodypart.SetActive(enabled);
            else
                r.enabled = enabled;
        }

        private List<TargetBodyparts> GetTargetBodyparts(NHItem item)
        {
            return item.Wrappers.Where(i => i.Enabled).Select(i => i.Type).ToList();
        }

        public NHAvatar Clean()
        {
            Cache.Clear();

            foreach (var e in PartsMap.Where(p => p.Target != null))
                ActivateBodyPart(e.Target, true);

            InitSkinMaterials();

            return this;
        }

        public void CleanCache(ItemTypeEnum type)
        {
            DestroyImmediate(Cache[type]);
            Cache.Remove(type);
        }

        private void NormalizeAnimationIndex()
        {
            if (_animationIndex <= -1)
                _animationIndex = _animationClips.Count - 1;
            else if (_animationIndex >= _animationClips.Count)
                _animationIndex = 0;
        }

        private void OnGUI()
        {
            if (!showAnimationControls || _animationClips.Count == 0)
                return;

            if (GUI.Button(new Rect(10, 10, 150, 20), "Prev"))
            {
                _animationIndex--;
                NormalizeAnimationIndex();
                _animator.CrossFade(_animationClips[_animationIndex].name, 0.1f);
            }

            GUIStyle clipNameStyle = new GUIStyle();
            Texture2D debugTex = new Texture2D(1, 1);
            debugTex.SetPixel(0, 0, Color.grey);
            debugTex.Apply();
            clipNameStyle.normal.background = debugTex;
            clipNameStyle.alignment = TextAnchor.MiddleCenter;
            clipNameStyle.normal.textColor = Color.white;

            GUI.Label(new Rect(170, 10, 150, 20), $"{_animationClips[_animationIndex].name} ▶️", clipNameStyle);

            if (GUI.Button(new Rect(330, 10, 150, 20), "Next"))
            {
                _animationIndex++;
                NormalizeAnimationIndex();
                _animator.CrossFade(_animationClips[_animationIndex].name, 0.1f);
            }
        }
#if UNITY_EDITOR
        public void Save()
        {
            //chose file path
            string filePath = EditorUtility.SaveFilePanel("Select Directory to save prefab", "Assets/StylizedCharacter/Prefabs", $"{gameObject.name}_Prefab", "prefab");
            if (string.IsNullOrEmpty(filePath))
                return;
            //copy current game object
            var copy = Instantiate(gameObject);
            //always enabled by default
            copy.gameObject.SetActive(true);
            //remove avatar
            copy.GetComponents<NHAvatar>().ToList().ForEach(i => DestroyImmediate(i));
            copy.GetComponentsInChildren<NHItem>().ToList().ForEach(i => DestroyImmediate(i));
            //save to prefab
            PrefabUtility.SaveAsPrefabAsset(copy, filePath);
            //destroy on scene
            DestroyImmediate(copy);
        }
#endif
        public void AutoSocketTargetSetup()
        {
            if(rootBone == null)
                return;
            
            foreach (BoneType type in Enum.GetValues(typeof(BoneType)))
                if (type != BoneType.None)
                {
                    var typeBoneName = type.TypeDescriptor().Name;
                    var bone = RecursiveFindChild(rootBone, typeBoneName);
                    if (bone != null)
                        SocketMap[type] = bone.transform;
                    else
                        SocketMap.Remove(type);
                }
        }

        Transform RecursiveFindChild(Transform parent, string childName)
        {
            foreach (Transform child in parent)
            {
                if(child.name.ToLower() == childName.ToLower())
                {
                    return child;
                }
                else
                {
                    Transform found = RecursiveFindChild(child, childName);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }
            return null;
        }
        
        public void AutoBodyPartsSetup()
        {
            if(rootGeometry == null)
                return;
            
            foreach (TargetBodyparts type in Enum.GetValues(typeof(TargetBodyparts)))
                if (!string.IsNullOrEmpty(type.TypeDescriptor().Name))
                {
                    var typeBoneName = type.TypeDescriptor().Name;
                    var bone = RecursiveFindChild(rootGeometry, typeBoneName);
                    var container = PartsMap.FirstOrDefault(t => t.Type == type);
                    if (container != null && bone != null)
                        container.Target = bone.gameObject;
                    else if (container != null)
                        container.Target = null;
                }
        }
    }
}