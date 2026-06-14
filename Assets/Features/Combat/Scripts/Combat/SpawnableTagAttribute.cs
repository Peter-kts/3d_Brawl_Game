using UnityEngine;

/// <summary>
/// Marks a GameObject field as expecting an object tagged "Spawnable".
/// The custom drawer warns in the Inspector if the assigned object lacks the tag.
/// </summary>
public class SpawnableTagAttribute : PropertyAttribute { }
