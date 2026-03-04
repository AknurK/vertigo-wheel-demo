using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class ChestDrop
{
    public WheelGameConfigSO.RewardType type;
    public int amount = 1;
    [Range(0f, 1f)] public float chance = 1f;
}

[CreateAssetMenu(menuName = "VertigoDemo/Wheel Slice", fileName = "so_slice_")]
public class WheelSlice : ScriptableObject
{
    public string id;

    [Header("UI")]
    public Sprite icon;

    [Header("Rules")]
    public bool isBomb;

    [Header("Reward")]
    public WheelGameConfigSO.RewardType rewardType;
    public int baseAmount; // Used for scalable "amount" rewards (e.g., Silver/Gold)

    [Header("Chest Drops (Only if rewardType == Chest)")]
    public List<ChestDrop> chestDrops = new List<ChestDrop>();
}