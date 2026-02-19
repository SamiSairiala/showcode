using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

using Skydome.Inventory;
using Skydome.Overworld;
using Skydome.UI;
using Skydome.Combat; // CombatCharacter + StatType

public class EquipmentMenuController : MonoBehaviour
{
	[Header("References")]
	public PlayerPartyManager partyManager;
	public Inventory inventory;

	[Header("Slot UIs")]
	public EquipmentSlotUI equipmentSlotWeaponUI;
	public EquipmentSlotUI equipmentSlotArmorUI;
	public EquipmentSlotUI accessory1SlotUI;
	public EquipmentSlotUI accessory2SlotUI;

	[Header("Slot Buttons (assign the Button on each slot)")]
	public Button weaponSlotButton;
	public Button armorSlotButton;
	public Button accessory1SlotButton;
	public Button accessory2SlotButton;

	[Header("Slot Selected Visuals (Outline)")]
	[SerializeField] private Outline weaponOutline;
	[SerializeField] private Outline armorOutline;
	[SerializeField] private Outline acc1Outline;
	[SerializeField] private Outline acc2Outline;

	[Header("Item List UI")]
	public EquipmentItemListUI equipmentItemListUI;

	[Header("Character UI")]
	public TextMeshProUGUI characterNameText;
	public Image characterImage;

	[Header("Character List UI")]
	[SerializeField] private EquipmentCharacterListUI characterListUI;

	[Tooltip("Optional portraits by party index (0..n-1). Leave empty if you don’t have portraits yet.")]
	[SerializeField] private List<Sprite> characterPortraits = new();
	[SerializeField] private Sprite defaultPortrait;

	[Header("Info Panel UI")]
	[SerializeField] private EquipmentInfoPanelUI infoPanelUI;
	[SerializeField] private bool startInfoPanelInactive = true;

	[Header("Attributes UI")]
	[SerializeField] private EquipmentAttributesUI attributesUI;

	[Tooltip("Pick which StatTypes you want displayed in the Attributes Anchor (must match your UI row order).")]
	[SerializeField] private List<StatType> statsToShow = new();

	private int _currentCharIndex;
	private PlayerCharacterRuntimeData _currentRuntimeData;

	private EquipmentTypes _currentSlotType = EquipmentTypes.Accessory; // sensible default for you
	private int _currentAccessoryIndex = 1; // default to accessory 1 for you

	private void OnEnable()
	{
		Open();
	}

	public void Open()
	{
		if (partyManager == null) partyManager = PlayerPartyManager.Instance;
		if (inventory == null) inventory = Inventory.Instance;

		if (partyManager == null || partyManager.PlayerInstances == null || partyManager.PlayerInstances.Count == 0)
			return;

		BuildCharacterListUI();

		SelectCharacter(0);
		ShowAllEquipmentList();

		// Highlighting an item in the list (keyboard/pad) updates the info panel
		if (equipmentItemListUI != null)
			equipmentItemListUI.SetOnItemSelected(ShowInfoForItem);

		// Keep these (don’t delete)
		SetSlotSelectedVisual(default, 0);
		if (equipmentItemListUI != null)
			equipmentItemListUI.SetCompatibilityFilter(null, autoSelectFirstCompatible: false);

		SetupSlotNavigation();

		// Start info panel hidden or empty
		if (infoPanelUI != null)
		{
			if (startInfoPanelInactive)
			{
				infoPanelUI.gameObject.SetActive(false);
			}
			else
			{
				infoPanelUI.gameObject.SetActive(true);
				infoPanelUI.Hide(); // start with nothing shown
			}
		}

		// Focus first usable slot button
		if (EventSystem.current != null)
		{
			var first = GetFirstSlotButton();
			if (first != null) EventSystem.current.SetSelectedGameObject(first.gameObject);
		}

		// Ensure info reflects the current slot/character
		ShowInfoForCurrentSlot();
	}

	private Button GetFirstSlotButton()
	{
		// Prefer trinkets if you aren't using weapon/armor
		if (accessory1SlotButton != null) return accessory1SlotButton;
		if (accessory2SlotButton != null) return accessory2SlotButton;
		if (weaponSlotButton != null) return weaponSlotButton;
		if (armorSlotButton != null) return armorSlotButton;
		return null;
	}

	private void EnsureInfoPanelActive()
	{
		if (infoPanelUI == null) return;
		if (!infoPanelUI.gameObject.activeSelf)
			infoPanelUI.gameObject.SetActive(true);
	}

	private void BuildCharacterListUI()
	{
		if (!characterListUI || partyManager == null || partyManager.PlayerInstances == null) return;

		int count = partyManager.PlayerInstances.Count;

		characterListUI.Build(
			count,
			i =>
			{
				var cc = partyManager.PlayerInstances[i];
				return cc != null ? cc.characterID : $"Character {i}";
			},
			i => GetPortraitForIndex(i),
			OnCharacterButtonSelected,
			_currentCharIndex,
			GetFirstSlotButton()
		);
	}

	private void OnCharacterButtonSelected(int index)
	{
		SelectCharacter(index);
		SetupSlotNavigation();

		// Move focus into slots after selecting a character
		if (EventSystem.current != null)
		{
			var first = GetFirstSlotButton();
			if (first != null) EventSystem.current.SetSelectedGameObject(first.gameObject);
		}
	}

	private void SetupSlotNavigation()
	{
		Button leftTarget = characterListUI != null ? characterListUI.GetButton(_currentCharIndex) : null;

		var slots = new List<Button>();
		if (weaponSlotButton != null) slots.Add(weaponSlotButton);
		if (armorSlotButton != null) slots.Add(armorSlotButton);
		if (accessory1SlotButton != null) slots.Add(accessory1SlotButton);
		if (accessory2SlotButton != null) slots.Add(accessory2SlotButton);

		for (int i = 0; i < slots.Count; i++)
		{
			Button up = slots[Mathf.Max(0, i - 1)];
			Button down = slots[Mathf.Min(slots.Count - 1, i + 1)];

			SetExplicitNav(slots[i], up, down, leftTarget, null);
		}
	}

	private static void SetExplicitNav(Button b, Button up, Button down, Button left, Button right)
	{
		if (b == null) return;

		var nav = new Navigation { mode = Navigation.Mode.Explicit };
		nav.selectOnUp = up;
		nav.selectOnDown = down;
		nav.selectOnLeft = left;
		nav.selectOnRight = right;
		b.navigation = nav;
	}

	private void ShowAllEquipmentList()
	{
		if (inventory == null) inventory = Inventory.Instance;
		if (inventory == null || equipmentItemListUI == null) return;

		var all = new List<Equipment>();
		all.AddRange(inventory.GetEquipmentsByType(EquipmentTypes.Weapon));
		all.AddRange(inventory.GetEquipmentsByType(EquipmentTypes.Armor));
		all.AddRange(inventory.GetEquipmentsByType(EquipmentTypes.Accessory));

		equipmentItemListUI.ShowItems(all, OnEquipItemChosen);
		equipmentItemListUI.SetEquippedItems(GetCurrentEquipped());
		equipmentItemListUI.SetInUseResolver(IsInUseNoSpare);
	}

	public void SelectCharacter(int index)
	{
		if (partyManager == null || partyManager.PlayerInstances == null || partyManager.PlayerInstances.Count == 0)
			return;

		_currentCharIndex = Mathf.Clamp(index, 0, partyManager.PlayerInstances.Count - 1);
		var cc = partyManager.PlayerInstances[_currentCharIndex];

		_currentRuntimeData = partyManager.GetRuntimeDataForIndex(_currentCharIndex);

		if (characterNameText != null && cc != null)
			characterNameText.text = cc.characterID;

		if (characterImage != null)
		{
			var portrait = GetPortraitForIndex(_currentCharIndex);
			characterImage.enabled = portrait != null;
			characterImage.sprite = portrait;
		}

		RefreshEquippedDisplay();
		RefreshAttributesDisplay();

		if (equipmentItemListUI != null)
		{
			equipmentItemListUI.SetCompatibilityFilter(null, autoSelectFirstCompatible: false);
			equipmentItemListUI.SetEquippedItems(GetCurrentEquipped());
			equipmentItemListUI.SetInUseResolver(IsInUseNoSpare);
		}

		SetSlotSelectedVisual(default, 0);

		if (characterListUI != null)
			characterListUI.SetSelected(_currentCharIndex);

		// DO NOT hide the panel here, otherwise it looks like it never updates.
		ShowInfoForCurrentSlot();
	}

	private void RefreshEquippedDisplay()
	{
		if (_currentRuntimeData == null)
		{
			equipmentSlotWeaponUI?.SetItem(null);
			equipmentSlotArmorUI?.SetItem(null);
			accessory1SlotUI?.SetItem(null);
			accessory2SlotUI?.SetItem(null);
			return;
		}

		var eq = _currentRuntimeData.equipment;

		equipmentSlotWeaponUI?.SetItem(eq.weapon);
		equipmentSlotArmorUI?.SetItem(eq.armor);
		accessory1SlotUI?.SetItem(eq.accessory1);
		accessory2SlotUI?.SetItem(eq.accessory2);
	}

	private void RefreshAttributesDisplay()
	{
		if (attributesUI == null) return;

		var cc = (partyManager != null && partyManager.PlayerInstances != null &&
				  _currentCharIndex >= 0 && _currentCharIndex < partyManager.PlayerInstances.Count)
			? partyManager.PlayerInstances[_currentCharIndex]
			: null;

		if (cc == null)
		{
			attributesUI.SetValues(null);
			attributesUI.SetDeltas(null);
			return;
		}

		attributesUI.SetValues(BuildCharacterAttributeStrings(cc));
		attributesUI.SetDeltas(null);
	}

	private void SetSlotSelectedVisual(EquipmentTypes type, int accIndex)
	{
		if (weaponOutline) weaponOutline.enabled = (type == EquipmentTypes.Weapon);
		if (armorOutline) armorOutline.enabled = (type == EquipmentTypes.Armor);
		if (acc1Outline) acc1Outline.enabled = (type == EquipmentTypes.Accessory && accIndex == 1);
		if (acc2Outline) acc2Outline.enabled = (type == EquipmentTypes.Accessory && accIndex == 2);
	}

	public void OnWeaponSlotSelected() => OnSlotSelected(EquipmentTypes.Weapon, 0);
	public void OnArmorSlotSelected() => OnSlotSelected(EquipmentTypes.Armor, 0);
	public void OnAccessory1SlotSelected() => OnSlotSelected(EquipmentTypes.Accessory, 1);
	public void OnAccessory2SlotSelected() => OnSlotSelected(EquipmentTypes.Accessory, 2);

	private void OnSlotSelected(EquipmentTypes slotType, int accessoryIndex)
	{
		if (inventory == null) inventory = Inventory.Instance;
		if (inventory == null || equipmentItemListUI == null) return;

		_currentSlotType = slotType;
		_currentAccessoryIndex = accessoryIndex;

		SetSlotSelectedVisual(slotType, accessoryIndex);

		switch (slotType)
		{
			case EquipmentTypes.Weapon:
				equipmentItemListUI.SetCompatibilityFilter(eq => eq.EquipmentType == EquipmentTypes.Weapon);
				break;
			case EquipmentTypes.Armor:
				equipmentItemListUI.SetCompatibilityFilter(eq => eq.EquipmentType == EquipmentTypes.Armor);
				break;
			case EquipmentTypes.Accessory:
				equipmentItemListUI.SetCompatibilityFilter(eq => eq.EquipmentType == EquipmentTypes.Accessory);
				break;
			default:
				equipmentItemListUI.SetCompatibilityFilter(null);
				break;
		}

		ShowInfoForCurrentSlot();
	}

	// Called by your EquipmentSlotSelectNotifier when the slot is highlighted
	public void SelectSlotFromUI(EquipmentTypes slotType, int accessoryIndex)
	{
		_currentSlotType = slotType;
		_currentAccessoryIndex = accessoryIndex;

		SetSlotSelectedVisual(slotType, accessoryIndex);
		ShowInfoForCurrentSlot();
	}

	private void ShowInfoForCurrentSlot()
	{
		if (infoPanelUI == null) return;

		EnsureInfoPanelActive();

		var eq = GetEquipmentInCurrentSlot();
		if (eq != null) infoPanelUI.Show(eq);
		else infoPanelUI.Hide();
	}

	private void ShowInfoForItem(Equipment eq)
	{
		if (infoPanelUI == null) return;

		EnsureInfoPanelActive();

		if (eq != null) infoPanelUI.Show(eq);
		else infoPanelUI.Hide();
	}

	private IEnumerable<Equipment> GetCurrentEquipped()
	{
		var e = _currentRuntimeData?.equipment;
		if (e == null) yield break;

		if (e.weapon) yield return e.weapon;
		if (e.armor) yield return e.armor;
		if (e.accessory1) yield return e.accessory1;
		if (e.accessory2) yield return e.accessory2;
	}

	private Sprite GetPortraitForIndex(int index)
	{
		if (characterPortraits != null &&
			index >= 0 && index < characterPortraits.Count &&
			characterPortraits[index] != null)
			return characterPortraits[index];

		return defaultPortrait;
	}

	private string Clean(string id)
	{
		if (inventory == null || string.IsNullOrEmpty(id)) return id;
		return inventory.CleanItemID(id);
	}

	private Equipment GetEquipmentInCurrentSlot()
	{
		var e = _currentRuntimeData?.equipment;
		if (e == null) return null;

		switch (_currentSlotType)
		{
			case EquipmentTypes.Weapon: return e.weapon;
			case EquipmentTypes.Armor: return e.armor;
			case EquipmentTypes.Accessory:
				return (_currentAccessoryIndex == 2) ? e.accessory2 : e.accessory1;
		}
		return null;
	}

	private void SetEquipmentInSlot(PlayerCharacterRuntimeData runtime, EquipmentTypes type, int accIndex, Equipment value)
	{
		var e = runtime?.equipment;
		if (e == null) return;

		switch (type)
		{
			case EquipmentTypes.Weapon: e.weapon = value; break;
			case EquipmentTypes.Armor: e.armor = value; break;
			case EquipmentTypes.Accessory:
				if (accIndex == 2) e.accessory2 = value;
				else e.accessory1 = value;
				break;
		}
	}

	private bool TryFindEquippedLocation(string cleanID, out int charIndex, out EquipmentTypes type, out int accIndex)
	{
		charIndex = -1;
		type = default;
		accIndex = 0;

		for (int i = 0; i < partyManager.PlayerInstances.Count; i++)
		{
			var runtime = partyManager.GetRuntimeDataForIndex(i);
			var e = runtime?.equipment;
			if (e == null) continue;

			if (e.weapon && Clean(e.weapon.ID) == cleanID) { charIndex = i; type = EquipmentTypes.Weapon; accIndex = 0; return true; }
			if (e.armor && Clean(e.armor.ID) == cleanID) { charIndex = i; type = EquipmentTypes.Armor; accIndex = 0; return true; }
			if (e.accessory1 && Clean(e.accessory1.ID) == cleanID) { charIndex = i; type = EquipmentTypes.Accessory; accIndex = 1; return true; }
			if (e.accessory2 && Clean(e.accessory2.ID) == cleanID) { charIndex = i; type = EquipmentTypes.Accessory; accIndex = 2; return true; }
		}

		return false;
	}

	private bool IsInUseNoSpare(Equipment e)
	{
		if (e == null || inventory == null || partyManager == null) return false;

		string cleanID = Clean(e.ID);
		int owned = inventory.GetItemCount(e);

		int equippedCount = 0;
		foreach (var r in partyManager.GetAllRuntimeEquipmentData())
		{
			if (r?.equipment == null) continue;

			if (r.equipment.weapon && Clean(r.equipment.weapon.ID) == cleanID) equippedCount++;
			if (r.equipment.armor && Clean(r.equipment.armor.ID) == cleanID) equippedCount++;
			if (r.equipment.accessory1 && Clean(r.equipment.accessory1.ID) == cleanID) equippedCount++;
			if (r.equipment.accessory2 && Clean(r.equipment.accessory2.ID) == cleanID) equippedCount++;
		}

		return equippedCount >= owned;
	}

	private void OnEquipItemChosen(Equipment equipment)
	{
		if (equipment == null) return;
		if (_currentRuntimeData == null) return;

		// auto-pick slot type if needed
		if (_currentSlotType != equipment.EquipmentType)
		{
			_currentSlotType = equipment.EquipmentType;

			if (equipment.EquipmentType == EquipmentTypes.Accessory && _currentAccessoryIndex == 0)
				_currentAccessoryIndex = 1;

			SetSlotSelectedVisual(_currentSlotType, _currentAccessoryIndex);
			equipmentItemListUI.SetCompatibilityFilter(eq => eq.EquipmentType == _currentSlotType, autoSelectFirstCompatible: false);
		}

		var currentInSlot = GetEquipmentInCurrentSlot();

		// toggle unequip
		if (currentInSlot != null && Clean(currentInSlot.ID) == Clean(equipment.ID))
		{
			SetEquipmentInSlot(_currentRuntimeData, _currentSlotType, _currentAccessoryIndex, null);

			var ccToggle = partyManager.PlayerInstances[_currentCharIndex];
			if (ccToggle != null)
				ccToggle.InitEquipmentFromPartyData(_currentRuntimeData);

			RefreshEquippedDisplay();
			RefreshAttributesDisplay();

			equipmentItemListUI.SetEquippedItems(GetCurrentEquipped());
			equipmentItemListUI.SetInUseResolver(IsInUseNoSpare);

			ShowInfoForCurrentSlot();
			return;
		}

		// swap if needed
		string clickedCleanID = Clean(equipment.ID);
		bool noSpare = IsInUseNoSpare(equipment);

		if (noSpare && TryFindEquippedLocation(clickedCleanID, out int fromChar, out EquipmentTypes fromType, out int fromAcc))
		{
			var fromRuntime = partyManager.GetRuntimeDataForIndex(fromChar);

			SetEquipmentInSlot(fromRuntime, fromType, fromAcc, currentInSlot);

			var fromCC = partyManager.PlayerInstances[fromChar];
			if (fromCC != null)
				fromCC.InitEquipmentFromPartyData(fromRuntime);
		}

		// equip
		SetEquipmentInSlot(_currentRuntimeData, _currentSlotType, _currentAccessoryIndex, equipment);

		var cc = partyManager.PlayerInstances[_currentCharIndex];
		if (cc != null)
			cc.InitEquipmentFromPartyData(_currentRuntimeData);

		RefreshEquippedDisplay();
		RefreshAttributesDisplay();

		equipmentItemListUI.SetEquippedItems(GetCurrentEquipped());
		equipmentItemListUI.SetInUseResolver(IsInUseNoSpare);

		ShowInfoForCurrentSlot();
		var ccDbg = partyManager.PlayerInstances[_currentCharIndex];
		if (ccDbg != null)
		{
			float baseVit = ccDbg.Info.GetFinalStat(StatType.Vitality);
			float vitBonus = GetEquipmentBonusForStat(StatType.Vitality, baseVit);
			Debug.Log($"[EQUIP MENU] baseVit={baseVit} bonusVit={vitBonus} finalVit={baseVit + vitBonus}");
		}
	}

	// ---------- Attributes ----------

	private List<string> BuildCharacterAttributeStrings(CombatCharacter cc)
	{
		var list = new List<string>();
		if (cc == null || statsToShow == null) return list;

		// Base values for display should come from info (stable in menus)
		// Then add equipment modifiers from runtime data (weapon/armor/trinkets)
		for (int i = 0; i < statsToShow.Count; i++)
		{
			var st = statsToShow[i];

			float baseVal = cc.Info != null ? cc.Info.GetFinalStat(st) : 0f;

			float equipAdd = GetEquipmentBonusForStat(st, baseVal);

			float final = baseVal + equipAdd;

			list.Add(FormatStat(final));
		}

		return list;
	}

	private float GetEquipmentBonusForStat(StatType stat, float baseValue)
	{
		float total = 0f;

		if (_currentRuntimeData?.equipment == null) return 0f;

		void AddFromEq(Equipment eq)
		{
			if (eq == null || eq.Stats == null) return;

			foreach (var s in eq.Stats)
			{
				if (s.StatToEffect != stat) continue;

				switch (s.BonusType)
				{
					case BonusType.Flat:
						total += s.BonusAmount;
						break;

					case BonusType.Percent:
						total += Mathf.Round((s.BonusAmount / 100f) * baseValue);
						break;
				}
			}
		}

		var e = _currentRuntimeData.equipment;
		AddFromEq(e.weapon);
		AddFromEq(e.armor);
		AddFromEq(e.accessory1);
		AddFromEq(e.accessory2);

		return total;
	}




	private string FormatStat(float v)
	{
		if (Mathf.Abs(v - Mathf.Round(v)) < 0.0001f)
			return Mathf.RoundToInt(v).ToString();

		return v.ToString("0.##");
	}
}
