using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Spin Wheel controller:
/// - Selects a slice based on zone rules (Normal / Safe / Super)
/// - Animates wheel rotation and snaps to the landed slice center
/// - Applies rewards (bank currencies, items, chest drops, bomb)
/// - Manages UI state (texts, button interactivity, popups, reward flyups)
///
/// Notes:
/// - Inspector field names are kept stable to preserve existing scene/prefab bindings.
/// - Visual order of slice UI is not changed; mapping is handled via wheelIndexToUiIndex.
/// </summary>
public class WheelController : MonoBehaviour
{
    // ---------------------------------------------------------------------
    // Types
    // ---------------------------------------------------------------------

    [Serializable]
    public class RewardResult
    {
        public WheelGameConfigSO.RewardType type;
        public int amount;

        public RewardResult(WheelGameConfigSO.RewardType type, int amount)
        {
            this.type = type;
            this.amount = amount;
        }
    }

    private enum ZoneType { Normal, Safe, Super }

    [Serializable]
    public class RewardIconMapping
    {
        public WheelGameConfigSO.RewardType type;

        [Tooltip("Randomly pick one of these icons for the given RewardType.")]
        public List<Sprite> icons = new List<Sprite>();
    }

    private enum RewardPopupFormat
    {
        PlusAmount,  // "+50"
        TimesAmount  // "x1"
    }

    private readonly struct QueuedPopup
    {
        public readonly Sprite icon;
        public readonly int amount;
        public readonly RewardPopupFormat format;

        public QueuedPopup(Sprite icon, int amount, RewardPopupFormat format)
        {
            this.icon = icon;
            this.amount = amount;
            this.format = format;
        }
    }

    // ---------------------------------------------------------------------
    // Inspector (keep names stable to preserve existing bindings)
    // ---------------------------------------------------------------------

    [Header("Configs")]
    [SerializeField] private WheelGameConfigSO gameConfig;
    [SerializeField] private WheelSetSO wheelSetSilver;
    [SerializeField] private WheelSetSO wheelSetGolden;
    [SerializeField] private WheelSetSO wheelSetSilverSafe;

    [Header("Wheel Transforms")]
    [SerializeField] private RectTransform wheelRootToRotate; // ui_anim_wheel_root

    [Header("Wheel Alignment")]
    [Tooltip("Base alignment offset (degrees) for the wheel art/prefab.")]
    [SerializeField] private float sliceAngleOffset = 0f;

    [Header("Pointer Visual Offset")]
    [Tooltip("Extra offset for pointer tip direction (degrees). Useful if the pointer is visually off-center.")]
    [SerializeField] private float pointerTipOffset = 0f;

    [Header("Wheel/UI Mapping (Does Not Change Visual Order)")]
    [Tooltip("wheelIndex: clockwise steps from the pointer (0..N-1). Value is UI slice index (sliceImages index).")]
    [SerializeField] private int[] wheelIndexToUiIndex = new int[8] { 0, 7, 6, 5, 4, 3, 2, 1 };

    [Header("Wheel Visual Swap")]
    [SerializeField] private Image wheelBaseImage;
    [SerializeField] private Sprite wheelBaseSilverSprite;
    [SerializeField] private Sprite wheelBaseGoldenSprite;

    [SerializeField] private Image indicatorImage;
    [SerializeField] private Sprite indicatorSilverSprite;
    [SerializeField] private Sprite indicatorGoldenSprite;

    [Header("Slice UI (8 items)")]
    [SerializeField] private Image[] sliceImages; // ui_slice_0..ui_slice_7 (scene order is fixed)

    [Header("UI Buttons")]
    [SerializeField] private Button spinButton;
    [SerializeField] private Button leaveButton;

    [Header("UI Texts")]
    [SerializeField] private TMP_Text zoneText;
    [SerializeField] private TMP_Text rewardText;
    [SerializeField] private TMP_Text bankText;

    [Header("Multiplier UI")]
    [SerializeField] private TMP_Text multiplierText; // ui_text_multiplier_value

    [Header("Reward Popup (Coins + Items)")]
    [SerializeField] private UIPopupCoinReward coinPopupPrefab; // ui_anim_coin_reward.prefab
    [SerializeField] private Transform coinPopupParent;         // Canvas child (popup layer)
    [SerializeField] private Sprite silverIcon;                 // Displayed as CASH
    [SerializeField] private Sprite goldIcon;
    [SerializeField] private RectTransform spinButtonRect;      // popup spawn position

    [Header("Reward Icons (Item Popups)")]
    [SerializeField] private List<RewardIconMapping> rewardIconMappings = new List<RewardIconMapping>();

    [Header("Reward Popup Queue")]
    [SerializeField] private bool useRewardPopupQueue = true;
    [SerializeField] private float rewardPopupInterval = 1.0f;

    [Header("Chest Popup (simple)")]
    [SerializeField] private GameObject chestPopupRoot;
    [SerializeField] private TMP_Text chestPopupTitle;
    [SerializeField] private TMP_Text chestPopupListText;
    [SerializeField] private Button chestPopupCloseButton;

    [Header("Bomb Popup (Revive)")]
    [SerializeField] private GameObject bombPopupRoot;
    [SerializeField] private TMP_Text bombPopupTitle;
    [SerializeField] private TMP_Text bombPopupSubtitle;
    [SerializeField] private Button bombGiveUpButton;
    [SerializeField] private Button bombReviveCoinButton;
    [SerializeField] private Button bombReviveAdsButton; // optional/placeholder

    [Header("Bomb Revive Cost")]
    [SerializeField] private int reviveGoldCost = 25;

    [Header("Audio")]
    public AudioSource spinAudio;

    [Header("Debug")]
    [SerializeField] private bool logLanding = false;

    // ---------------------------------------------------------------------
    // Runtime state
    // ---------------------------------------------------------------------

    private int currentZone = 1;

    private bool isSpinning = false;
    private bool isPopupOpen = false;

    // Internal names remain stable; display naming is handled via GetRewardDisplayName().
    private int bankSilver = 0; // Displayed as CASH
    private int bankGold = 0;

    private ZoneType lastZoneType = ZoneType.Normal;

    private readonly Queue<QueuedPopup> popupQueue = new Queue<QueuedPopup>();
    private Coroutine popupQueueRoutine;

    private Dictionary<WheelGameConfigSO.RewardType, List<Sprite>> rewardIconMap;

    // In Unity UI, negative Z rotation appears as clockwise (depending on your wheel art orientation).
    private const int CLOCKWISE_DIR = -1;

    // ---------------------------------------------------------------------
    // Unity lifecycle
    // ---------------------------------------------------------------------

    private void Awake()
    {
        if (spinButton) spinButton.onClick.AddListener(Spin);
        if (leaveButton) leaveButton.onClick.AddListener(Leave);

        if (chestPopupCloseButton) chestPopupCloseButton.onClick.AddListener(CloseChestPopup);
        if (chestPopupRoot) chestPopupRoot.SetActive(false);

        if (bombGiveUpButton) bombGiveUpButton.onClick.AddListener(GiveUpAfterBomb);
        if (bombReviveCoinButton) bombReviveCoinButton.onClick.AddListener(ReviveAfterBombWithCoin);
        if (bombReviveAdsButton) bombReviveAdsButton.onClick.AddListener(ReviveAfterBombWithAds);
        if (bombPopupRoot) bombPopupRoot.SetActive(false);

        if (spinAudio != null)
        {
            spinAudio.playOnAwake = false;
            spinAudio.loop = true;
        }

        if (!spinButtonRect && spinButton)
            spinButtonRect = spinButton.GetComponent<RectTransform>();

        BuildRewardIconMap();
        WarnIfMissingRefs();
    }

    private void Start()
    {
        lastZoneType = GetZoneType(currentZone);
        RefreshAllUI();
    }

    // ---------------------------------------------------------------------
    // Main flow
    // ---------------------------------------------------------------------

    private void Spin()
    {
        if (isSpinning || isPopupOpen) return;

        if (!ValidateRefsHard())
        {
            Debug.LogError("WheelController: Missing critical references. Check the Inspector.");
            return;
        }

        ZoneType zoneType = GetZoneType(currentZone);
        WheelSetSO set = GetWheelSetForZone(zoneType);

        ApplyWheelVisuals(zoneType);
        ApplySliceIconsStable(set);

        bool bombAllowed = (zoneType == ZoneType.Normal);

        WheelSlice chosen = ChooseSlice(set, bombAllowed);
        int chosenDataIndex = GetSliceIndex(set, chosen);

        StartCoroutine(SpinRoutine(set, chosenDataIndex));
    }

    private IEnumerator SpinRoutine(WheelSetSO set, int chosenDataIndex)
    {
        isSpinning = true;
        SetButtonsInteractable(false);

        if (spinAudio != null)
        {
            spinAudio.Stop();
            spinAudio.Play();
        }

        float duration = Mathf.Max(0.1f, gameConfig.spinDuration);
        int fullRotations = UnityEngine.Random.Range(gameConfig.minFullRotations, gameConfig.maxFullRotations + 1);

        int sliceCount = Mathf.Max(1, set.slices.Count);
        float anglePerSlice = 360f / sliceCount;

        if (wheelRootToRotate) wheelRootToRotate.localEulerAngles = Vector3.zero;

        int targetWheelIndex = UiIndexToWheelIndex(chosenDataIndex, sliceCount);
        float totalOffset = sliceAngleOffset + pointerTipOffset;

        float targetAngleToCenter = (targetWheelIndex * anglePerSlice) + (anglePerSlice * 0.5f) + totalOffset;

        float startZ = 0f;
        float endZ = CLOCKWISE_DIR * (fullRotations * 360f + targetAngleToCenter);

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / duration;

            float eased = EaseOutCubic(Mathf.Clamp01(t));
            float z = Mathf.Lerp(startZ, endZ, eased);

            if (wheelRootToRotate)
                wheelRootToRotate.localEulerAngles = new Vector3(0, 0, z);

            yield return null;
        }

        int landedWheelIndex = GetLandedWheelIndex(set);
        SnapWheelToWheelIndexCenter(landedWheelIndex, sliceCount);

        landedWheelIndex = GetLandedWheelIndex(set);
        int landedUiIndex = WheelIndexToUiIndex(landedWheelIndex, sliceCount);

        if (logLanding)
        {
            float zNow = wheelRootToRotate ? wheelRootToRotate.localEulerAngles.z : 0f;
            Debug.Log(
                $"LANDING => chosenUI:{chosenDataIndex} landedWheel:{landedWheelIndex} landedUI:{landedUiIndex} " +
                $"z:{zNow:0.00} totalOffset:{(sliceAngleOffset + pointerTipOffset):0.00}"
            );
        }

        WheelSlice landedSlice =
            (landedUiIndex >= 0 && landedUiIndex < set.slices.Count) ? set.slices[landedUiIndex] : null;

        ApplyResult(landedSlice);
        RefreshAllUI();

        if (spinAudio != null)
            spinAudio.Stop();

        isSpinning = false;
        SetButtonsInteractable(true);
    }

    private WheelSetSO GetWheelSetForZone(ZoneType zoneType)
    {
        return (zoneType == ZoneType.Super) ? wheelSetGolden :
               (zoneType == ZoneType.Safe) ? wheelSetSilverSafe :
                                             wheelSetSilver;
    }

    // ---------------------------------------------------------------------
    // Visuals (stable UI order)
    // ---------------------------------------------------------------------

    private void ApplyWheelVisuals(ZoneType zoneType)
    {
        bool golden = (zoneType == ZoneType.Super);

        if (wheelBaseImage)
            wheelBaseImage.sprite = golden ? wheelBaseGoldenSprite : wheelBaseSilverSprite;

        if (indicatorImage)
            indicatorImage.sprite = golden ? indicatorGoldenSprite : indicatorSilverSprite;
    }

    /// <summary>
    /// Does not reorder UI. ui_slice_i always uses set.slices[i].
    /// </summary>
    private void ApplySliceIconsStable(WheelSetSO set)
    {
        if (sliceImages == null || sliceImages.Length == 0 || set == null || set.slices == null) return;

        for (int i = 0; i < sliceImages.Length; i++)
        {
            Image img = sliceImages[i];
            if (!img) continue;

            if (i < set.slices.Count && set.slices[i] != null)
            {
                img.sprite = set.slices[i].icon;
                img.enabled = (img.sprite != null);
            }
        }
    }

    // ---------------------------------------------------------------------
    // Wheel index mapping
    // ---------------------------------------------------------------------

    private static int Mod(int a, int n) => (a % n + n) % n;

    private int WheelIndexToUiIndex(int wheelIndex, int sliceCount)
    {
        if (sliceCount <= 0) return 0;

        if (wheelIndexToUiIndex == null || wheelIndexToUiIndex.Length < sliceCount)
            return Mod(wheelIndex, sliceCount);

        return Mod(wheelIndexToUiIndex[Mod(wheelIndex, sliceCount)], sliceCount);
    }

    private int UiIndexToWheelIndex(int uiIndex, int sliceCount)
    {
        if (sliceCount <= 0) return 0;

        uiIndex = Mod(uiIndex, sliceCount);

        if (wheelIndexToUiIndex == null || wheelIndexToUiIndex.Length < sliceCount)
            return uiIndex;

        for (int w = 0; w < sliceCount; w++)
        {
            if (Mod(wheelIndexToUiIndex[w], sliceCount) == uiIndex)
                return w;
        }

        return uiIndex;
    }

    // ---------------------------------------------------------------------
    // Pointer / landing + snap
    // ---------------------------------------------------------------------

    private float GetPointerAngleDeg()
    {
        float z = wheelRootToRotate ? wheelRootToRotate.localEulerAngles.z : 0f;
        float totalOffset = sliceAngleOffset + pointerTipOffset;
        return Mathf.Repeat(-z - totalOffset, 360f);
    }

    private int GetLandedWheelIndex(WheelSetSO set)
    {
        int sliceCount = Mathf.Max(1, set.slices.Count);
        float anglePerSlice = 360f / sliceCount;

        float pointerAngle = GetPointerAngleDeg();
        return Mathf.FloorToInt((pointerAngle + anglePerSlice * 0.5f) / anglePerSlice) % sliceCount;
    }

    private void SnapWheelToWheelIndexCenter(int wheelIndex, int sliceCount)
    {
        if (!wheelRootToRotate || sliceCount <= 0) return;

        float anglePerSlice = 360f / sliceCount;

        float desiredPointerAngle = (wheelIndex * anglePerSlice) + (anglePerSlice * 0.5f);
        float currentPointerAngle = GetPointerAngleDeg();

        float diff = Mathf.DeltaAngle(currentPointerAngle, desiredPointerAngle);

        float z = wheelRootToRotate.localEulerAngles.z;
        wheelRootToRotate.localEulerAngles = new Vector3(0, 0, z - diff);
    }

    // ---------------------------------------------------------------------
    // Results / rewards
    // ---------------------------------------------------------------------

    private void ApplyResult(WheelSlice slice)
    {
        if (slice == null)
        {
            SetRewardText("REWARD: -");
            return;
        }

        if (slice.isBomb)
        {
            SetRewardText("REWARD: BOMB!");
            OpenBombPopup();
            return;
        }

        if (IsChest(slice))
        {
            List<RewardResult> rewards = ResolveChestRewards(slice);

            foreach (RewardResult r in rewards)
            {
                if (IsBankReward(r.type))
                {
                    AddToBank(r.type, r.amount);
                }
                else
                {
                    EnqueueRewardPopupForType(r.type, r.amount, RewardPopupFormat.TimesAmount);
                    Debug.Log($"ITEM COLLECTED: {r.type} x{r.amount}");
                }
            }

            OpenChestPopup(rewards);

            currentZone++;
            OnZoneAdvanced();
            SetRewardText("REWARD: CHEST");
            return;
        }

        int finalAmount = GetScaledAmount(slice.baseAmount, currentZone);

        if (IsBankReward(slice.rewardType))
        {
            AddToBank(slice.rewardType, finalAmount);
        }
        else
        {
            EnqueueRewardPopupForType(slice.rewardType, finalAmount, RewardPopupFormat.TimesAmount);
            Debug.Log($"ITEM COLLECTED: {slice.rewardType} x{finalAmount}");
        }

        string displayName = GetRewardDisplayName(slice.rewardType);
        SetRewardText($"REWARD: {displayName} {finalAmount}");

        currentZone++;
        OnZoneAdvanced();
    }

    private void OnZoneAdvanced()
    {
        // Zone badge popup removed (UI was deleted). Keep the hook for future zone-related effects if needed.
        lastZoneType = GetZoneType(currentZone);
    }

    private void SetRewardText(string text)
    {
        if (rewardText) rewardText.text = text;
    }

    private static string GetRewardDisplayName(WheelGameConfigSO.RewardType type)
    {
        if (type == WheelGameConfigSO.RewardType.Silver) return "CASH";
        return type.ToString().ToUpperInvariant();
    }

    // ---------------------------------------------------------------------
    // Icon map + popup queue
    // ---------------------------------------------------------------------

    private void BuildRewardIconMap()
    {
        rewardIconMap = new Dictionary<WheelGameConfigSO.RewardType, List<Sprite>>();

        if (rewardIconMappings != null)
        {
            foreach (RewardIconMapping m in rewardIconMappings)
            {
                if (m == null || m.icons == null || m.icons.Count == 0) continue;

                List<Sprite> cleaned = m.icons.Where(x => x != null).ToList();
                if (cleaned.Count == 0) continue;

                rewardIconMap[m.type] = cleaned;
            }
        }

        if (silverIcon) rewardIconMap[WheelGameConfigSO.RewardType.Silver] = new List<Sprite> { silverIcon };
        if (goldIcon) rewardIconMap[WheelGameConfigSO.RewardType.Gold] = new List<Sprite> { goldIcon };
    }

    private Sprite GetRewardIcon(WheelGameConfigSO.RewardType type)
    {
        if (rewardIconMap == null) BuildRewardIconMap();

        if (rewardIconMap != null &&
            rewardIconMap.TryGetValue(type, out List<Sprite> list) &&
            list != null && list.Count > 0)
        {
            int idx = UnityEngine.Random.Range(0, list.Count);
            return list[idx];
        }

        return null;
    }

    private void EnqueueRewardPopupForType(WheelGameConfigSO.RewardType type, int amount, RewardPopupFormat format)
    {
        Sprite icon = GetRewardIcon(type);
        if (!icon) return;

        EnqueueRewardPopup(icon, amount, format);
    }

    private void EnqueueRewardPopup(Sprite icon, int amount, RewardPopupFormat format)
    {
        if (!coinPopupPrefab || !coinPopupParent) return;

        if (!useRewardPopupQueue)
        {
            SpawnRewardPopup(icon, amount, format);
            return;
        }

        popupQueue.Enqueue(new QueuedPopup(icon, amount, format));

        if (popupQueueRoutine == null)
            popupQueueRoutine = StartCoroutine(PopupQueueRoutine());
    }

    private IEnumerator PopupQueueRoutine()
    {
        while (popupQueue.Count > 0)
        {
            QueuedPopup item = popupQueue.Dequeue();
            SpawnRewardPopup(item.icon, item.amount, item.format);

            yield return new WaitForSeconds(Mathf.Max(0.05f, rewardPopupInterval));
        }

        popupQueueRoutine = null;
    }

    private void SpawnRewardPopup(Sprite icon, int amount, RewardPopupFormat format)
    {
        UIPopupCoinReward popup = Instantiate(coinPopupPrefab, coinPopupParent);

        if (spinButtonRect) popup.transform.position = spinButtonRect.position;
        else popup.transform.localPosition = Vector3.zero;

        if (format == RewardPopupFormat.PlusAmount)
            popup.Play(icon, amount);
        else
            popup.Play(icon, $"x{amount}");
    }

    // ---------------------------------------------------------------------
    // Popups
    // ---------------------------------------------------------------------

    private void OpenBombPopup()
    {
        if (!bombPopupRoot)
        {
            ResetGameAfterBomb();
            return;
        }

        isPopupOpen = true;
        bombPopupRoot.SetActive(true);

        if (bombPopupTitle) bombPopupTitle.text = "OH NO, A BOMB EXPLODED RIGHT IN YOUR HANDS!";
        if (bombPopupSubtitle) bombPopupSubtitle.text = "Revive yourself to keep your rewards.";

        if (bombReviveCoinButton)
            bombReviveCoinButton.interactable = (bankGold >= reviveGoldCost);

        SetButtonsInteractable(false);
    }

    private void CloseBombPopup()
    {
        if (!bombPopupRoot) return;

        bombPopupRoot.SetActive(false);
        isPopupOpen = false;

        RefreshAllUI();
        SetButtonsInteractable(true);
    }

    private void GiveUpAfterBomb()
    {
        CloseBombPopup();
        ResetGameFull();
        RefreshAllUI();
    }

    private void ReviveAfterBombWithCoin()
    {
        // Button should already be disabled when the player cannot afford it.
        if (bankGold < reviveGoldCost) return;

        bankGold -= reviveGoldCost;
        CloseBombPopup();
        RefreshAllUI();
    }

    private void ReviveAfterBombWithAds()
    {
        Debug.Log("ADS REVIVE clicked (placeholder).");
        CloseBombPopup();
        RefreshAllUI();
    }

    private void OpenChestPopup(List<RewardResult> rewards)
    {
        if (!chestPopupRoot) return;

        isPopupOpen = true;
        chestPopupRoot.SetActive(true);

        if (chestPopupTitle) chestPopupTitle.text = "CHEST REWARDS";

        if (chestPopupListText)
        {
            chestPopupListText.text = (rewards == null || rewards.Count == 0)
                ? "-"
                : string.Join("\n", rewards.Select(r => $"{GetRewardDisplayName(r.type)} x{r.amount}"));
        }

        SetButtonsInteractable(false);
    }

    private void CloseChestPopup()
    {
        if (!chestPopupRoot) return;

        chestPopupRoot.SetActive(false);
        isPopupOpen = false;

        RefreshAllUI();
        SetButtonsInteractable(true);
    }

    // ---------------------------------------------------------------------
    // Chest
    // ---------------------------------------------------------------------

    private bool IsChest(WheelSlice slice)
    {
        return slice != null && slice.rewardType == WheelGameConfigSO.RewardType.Chest;
    }

    private List<RewardResult> ResolveChestRewards(WheelSlice chestSlice)
    {
        var results = new List<RewardResult>();
        if (chestSlice == null || chestSlice.chestDrops == null) return results;

        foreach (var d in chestSlice.chestDrops)
        {
            if (d == null) continue;

            float roll = UnityEngine.Random.value;
            if (roll <= Mathf.Clamp01(d.chance))
            {
                int amount = Mathf.Max(1, d.amount);

                if (IsBankReward(d.type))
                    amount = GetScaledAmount(amount, currentZone);

                results.Add(new RewardResult(d.type, amount));
            }
        }

        if (results.Count == 0)
            results.Add(new RewardResult(WheelGameConfigSO.RewardType.Silver, GetScaledAmount(10, currentZone)));

        return results;
    }

    // ---------------------------------------------------------------------
    // Zone rules
    // ---------------------------------------------------------------------

    private ZoneType GetZoneType(int zone)
    {
        if (gameConfig == null) return ZoneType.Normal;

        if (zone > 0 && gameConfig.superZoneEvery > 0 && zone % gameConfig.superZoneEvery == 0) return ZoneType.Super;
        if (zone > 0 && gameConfig.safeZoneEvery > 0 && zone % gameConfig.safeZoneEvery == 0) return ZoneType.Safe;

        return ZoneType.Normal;
    }

    private void ApplyZoneFx(ZoneType zoneType)
    {
        // Optional: if you want to keep wheel flashes when entering Safe/Super.
        // This method currently does nothing to keep behavior minimal after removing the badge UI.
    }

    // ---------------------------------------------------------------------
    // Bank
    // ---------------------------------------------------------------------

    private bool IsBankReward(WheelGameConfigSO.RewardType type)
    {
        return type == WheelGameConfigSO.RewardType.Silver
            || type == WheelGameConfigSO.RewardType.Gold;
    }

    private void AddToBank(WheelGameConfigSO.RewardType type, int amount)
    {
        amount = Mathf.Max(0, amount);

        switch (type)
        {
            case WheelGameConfigSO.RewardType.Silver:
                bankSilver += amount;
                EnqueueRewardPopupForType(WheelGameConfigSO.RewardType.Silver, amount, RewardPopupFormat.PlusAmount);
                break;

            case WheelGameConfigSO.RewardType.Gold:
                bankGold += amount;
                EnqueueRewardPopupForType(WheelGameConfigSO.RewardType.Gold, amount, RewardPopupFormat.PlusAmount);
                break;
        }
    }

    private void Leave()
    {
        if (isSpinning || isPopupOpen) return;
        if (!IsLeaveAllowed(currentZone)) return;

        ResetGameFull();
        RefreshAllUI();
    }

    private bool IsLeaveAllowed(int zone)
    {
        ZoneType zt = GetZoneType(zone);
        return (zt == ZoneType.Safe || zt == ZoneType.Super);
    }

    private void ResetGameAfterBomb()
    {
        ResetGameFull();
        Debug.Log("BOMB! Bank reset. Game restarted (zone=1).");
    }

    private void ResetGameFull()
    {
        currentZone = 1;
        bankSilver = 0;
        bankGold = 0;

        if (wheelRootToRotate) wheelRootToRotate.localEulerAngles = Vector3.zero;

        if (chestPopupRoot) chestPopupRoot.SetActive(false);
        if (bombPopupRoot) bombPopupRoot.SetActive(false);

        isPopupOpen = false;

        lastZoneType = GetZoneType(currentZone);

        popupQueue.Clear();
        if (popupQueueRoutine != null)
        {
            StopCoroutine(popupQueueRoutine);
            popupQueueRoutine = null;
        }
    }

    // ---------------------------------------------------------------------
    // UI
    // ---------------------------------------------------------------------

    private void RefreshAllUI()
    {
        if (zoneText) zoneText.text = $"ZONE: {currentZone}";

        float mult = (gameConfig != null)
            ? (1f + ((Mathf.Max(1, currentZone) - 1) * gameConfig.scalePerZone))
            : 1f;

        if (multiplierText) multiplierText.text = $"x{mult:0.0}";

        if (rewardText && string.IsNullOrEmpty(rewardText.text))
            rewardText.text = "REWARD: -";

        if (bankText)
            bankText.text = $"BANK:  {bankSilver}      {bankGold}";

        ZoneType zt = GetZoneType(currentZone);
        ApplyWheelVisuals(zt);
        ApplySliceIconsStable(GetWheelSetForZone(zt));

        UpdateLeaveButtonState();
    }

    private void UpdateLeaveButtonState()
    {
        if (!leaveButton) return;
        leaveButton.interactable = (!isSpinning && !isPopupOpen && IsLeaveAllowed(currentZone));
    }

    private void SetButtonsInteractable(bool interactable)
    {
        if (spinButton) spinButton.interactable = interactable && !isSpinning && !isPopupOpen;
        UpdateLeaveButtonState();
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private int GetScaledAmount(int baseAmount, int zone)
    {
        float mult = 1f + ((Mathf.Max(1, zone) - 1) * gameConfig.scalePerZone);
        return Mathf.RoundToInt(baseAmount * mult);
    }

    private static float EaseOutCubic(float x) => 1f - Mathf.Pow(1f - x, 3f);

    private WheelSlice ChooseSlice(WheelSetSO set, bool bombAllowed)
    {
        if (set == null || set.slices == null || set.slices.Count == 0)
            return null;

        List<WheelSlice> list = set.slices.Where(s => s != null).ToList();

        if (!bombAllowed)
            list = list.Where(s => !s.isBomb).ToList();

        if (list.Count == 0)
            return set.slices.FirstOrDefault(s => s != null);

        return list[UnityEngine.Random.Range(0, list.Count)];
    }

    private static int GetSliceIndex(WheelSetSO set, WheelSlice slice)
    {
        if (set == null || set.slices == null || slice == null) return 0;
        int idx = set.slices.IndexOf(slice);
        return (idx < 0) ? 0 : idx;
    }

    // ---------------------------------------------------------------------
    // Reference validation
    // ---------------------------------------------------------------------

    private void WarnIfMissingRefs()
    {
        if (!multiplierText) Debug.LogWarning("WheelController: multiplierText is not set.");
        if (!coinPopupPrefab) Debug.LogWarning("WheelController: coinPopupPrefab is not set.");
        if (!coinPopupParent) Debug.LogWarning("WheelController: coinPopupParent is not set.");
        if (!silverIcon) Debug.LogWarning("WheelController: cash icon (silverIcon) is not set.");
        if (!goldIcon) Debug.LogWarning("WheelController: goldIcon is not set.");
    }

    private bool ValidateRefsHard()
    {
        if (!gameConfig) return false;
        if (!wheelSetSilver || !wheelSetGolden || !wheelSetSilverSafe) return false;
        if (!wheelRootToRotate) return false;

        if (!spinButton || !leaveButton) return false;
        if (!zoneText || !rewardText || !bankText) return false;

        if (!wheelBaseImage || !indicatorImage) return false;
        if (!wheelBaseSilverSprite || !wheelBaseGoldenSprite) return false;
        if (!indicatorSilverSprite || !indicatorGoldenSprite) return false;

        if (sliceImages == null || sliceImages.Length < 8) return false;

        return true;
    }
}