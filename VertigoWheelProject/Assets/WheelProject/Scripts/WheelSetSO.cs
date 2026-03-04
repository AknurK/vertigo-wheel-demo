using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "VertigoDemo/Wheel Set", fileName = "so_wheelset_")]
public class WheelSetSO : ScriptableObject
{
    public List<WheelSlice> slices = new List<WheelSlice>();
}