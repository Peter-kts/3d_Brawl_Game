using System;
using System.Collections.Generic;
using System.Linq;
using NHance.Assets.Scripts.Attributes;
using NHance.Assets.Scripts.Enums;

namespace NHance.Assets.Scripts.Utils
{
    public class ItemTypeQuery
        {
            private ItemTypeDescriptorAttribute _options = new ItemTypeDescriptorAttribute();
            private OrderType _order = OrderType.None;
            private List<ItemTypeEnum> _exclude = new List<ItemTypeEnum>();
            private Gender _excludeGender = Gender.All;
            private SocketingType _socketingType = SocketingType.All;
            private string Filter;

            public List<ItemTypeEnum> Build()
            {
                List<ItemTypeEnum> result = new List<ItemTypeEnum>();
                foreach (ItemTypeEnum type in Enum.GetValues(typeof(ItemTypeEnum)))
                    result.Add(type);

                var indexer = result.Select(i => i);

                indexer = indexer.Where(t => t.TypeDescriptor().Type == _options.Type);
                if (_options.PersistInGender != Gender.All)
                    indexer = indexer.Where(t => t.TypeDescriptor().PersistInGender == _options.PersistInGender || t.TypeDescriptor().PersistInGender == Gender.All);

                if (_options.Namespace != "")
                {
                    var gns = _options.Namespace.Split(',').Select(n => n.ToLower().Trim());
                    List<ItemTypeEnum> toFilter = new List<ItemTypeEnum>();
                    foreach (var item in indexer)
                    {
                        foreach (var ns in item.TypeDescriptor().Namespace.Split(',').Select(n => n.ToLower().Trim()))
                        {
                            if (gns.Contains(ns))
                            {
                                toFilter.Add(item);
                                break;
                            }
                        }
                    }

                    indexer = toFilter.Select(t => t);
                }

                if (_excludeGender != Gender.All)
                    indexer = indexer.Where(t =>
                        t.TypeDescriptor().PersistInGender != Gender.All &&
                        t.TypeDescriptor().PersistInGender != _excludeGender);

                if (!string.IsNullOrEmpty(Filter))
                    indexer = indexer.Where(t => t.TypeDescriptor().Category == Filter);
                    
                if (_exclude.Count > 0)
                    indexer = indexer.Where(t => !_exclude.Contains(t));

                if (_socketingType != SocketingType.All)
                    indexer = indexer.Where(t =>
                        _socketingType == SocketingType.CanBeInSocket && t.TypeDescriptor().IsCanBeInSocket || _socketingType == SocketingType.NotCanBeInSocket && !t.TypeDescriptor().IsCanBeInSocket);
                
                if (_order == OrderType.Ascending)
                    indexer = indexer.OrderBy(t => t.TypeDescriptor().Order);
                else
                    indexer = indexer.OrderByDescending(t => t.TypeDescriptor().Order);

                return indexer.ToList();
            }

            public ItemTypeQuery WithOrder(OrderType order)
            {
                _order = order;
                return this;
            }
            public ItemTypeQuery WithCategory(ItemCategory category)
            {
                _options.Type = category;
                return this;
            }
            public ItemTypeQuery WithFilter(string filter)
            {
                Filter = filter;
                return this;
            }
            public ItemTypeQuery WithGender(Gender gender)
            {
                _options.PersistInGender = gender;
                return this;
            }
            public ItemTypeQuery ExcludeGender(Gender gender)
            {
                _excludeGender = gender;
                return this;
            }
            
            public ItemTypeQuery WithNamespace(string nameSpace)
            {
                _options.Namespace = nameSpace;
                return this;
            }
            public ItemTypeQuery Exclude(params ItemTypeEnum[] types)
            {
                _exclude.AddRange(types);
                return this;
            }

            public ItemTypeQuery ItemCanBeInSocket(SocketingType type)
            {
                _socketingType = type;
                return this;
            }
            
        }
}