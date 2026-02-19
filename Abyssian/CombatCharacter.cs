using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FMODUnity;
using Skydome.Animations;
using Skydome.Audio;
using Skydome.Inventory;
using Skydome.Overworld;
using Skydome.Rendering.Effects;
using Skydome.Tools;
using Skydome.UI;
using UnityEditor;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Skydome.Combat
{
	public class CombatCharacter : MonoBehaviour
	{
		public enum CharacterClassForInspector
		{
			Player,
			Enemy
		}

		readonly int TARGET_SPRITE_INDEX = 0, TARGET_ARROW_INDEX = 1, DAMAGE_NUMBER_INDEX = 2;

		#region editor variables

		[Header("Character info/Put abilities and Attacks in the info")]
		[SerializeField]
		public CombatCharacterInfo _info;

		public CharacterClassForInspector characterClassForInspector;

		[Tooltip("The percentage of HP where character go to low HP state")]
		[SerializeField, Range(0, 1)]
		float _lowHP = 0.25f;

		[Tooltip("The percentage of HP where character go to yellow HP state")]
		[SerializeField, Range(0, 1)]
		float _mediumHP = 0.51f;

		[Space(5)]
		[Header("Combat variables")]
		[Tooltip("The sprite color for damages in enemy characters")]
		[SerializeField]
		Color _damageColor;

		[SerializeField] float _damageAnimationTime;
		[SerializeField] public Material dissolveMaterial;

		[Header("EnemyClass Attack effect if is null will use BiteEffect")]
		[Header("Dosen't affect Player Chars")]
		[HideInInspector]
		public GameObject AttackEffectEnemy;

		[Space]
		[Header("If want to start with token for example Blindness in turn 1")]
		[SerializeField]
		private EffectValue[] StartingTokens;

		[HideInInspector][SerializeField] public EffectValue[] TempStartingTokensOnPlayers;

		[Space] public VFXPrefabHolder VFXPrefabHolder;

		[Space]
		//[HideInInspector] public CombatCharacterEffectIcons ccei;
		[Header("Only assign on player chars. Enemies will get sound from attack actions.")]
		[HideInInspector]
		public StudioEventEmitter attackSound;

		[HideInInspector] public StudioEventEmitter hurtSound;

		private GameObject StunEffect;

		#endregion

		#region public

		// turn order
		/// <summary>
		/// Use only from turn ordering methods!
		/// </summary>
		[HideInInspector] public float TotalSpeed;

		/// <summary>
		/// Use only from turn ordering methods!
		/// </summary>
		[HideInInspector] public int TurnsStartedPreview, TurnsStarted;

		[HideInInspector] public List<float> totalSpeedAtTurn = new();
		[HideInInspector] public List<float> tempTotalSpeedAtTurn = new();
		[HideInInspector] public List<float> turnCounterAtTurn = new();
		[HideInInspector] public List<float> tempTurnCounterAtTurn = new();
		[HideInInspector] public Dictionary<int, float> turnsStartedTotalSpeed = new();
		[HideInInspector] public AnimationEvent CurrentTurnOrderIcon, NextTurnOrderIcon;

		[HideInInspector] public AnimatorExtension spriteAnimator;

		// other
		/*[HideInInspector]*/
		public List<Effect> CurrentEffects = new(); // Temp not hidden for bug fixing.
		[HideInInspector] public List<ItemSlot> ItemSlots = new();
		[HideInInspector] public int TurnsCompleted;
		[HideInInspector] public bool startingTurn;

		public Vector3 stunOffset = new Vector3(0, 1.5f, 0);
		private bool showingNumbers;

		//Currently testing character XP
		public int CurrentLevel { get; set; } = 1;
		public int CurrentXP { get; set; } = 0;
		public int CurrentTargetXP { get; private set; } = 10;

		public SetAbilityAnim AbilityAnimController;

		[Header("Assign on enemies eg 70% chance for basic")]
		public int basicAttackWeight = 70;
		public int spellAttackWeight = 30;

		#endregion

		#region local

		// stat
		/// <summary>
		/// For custom editor only
		/// </summary>
		[HideInInspector] public int level = 1;

		BaseStat[] _stats;

		// turn
		public CombatAction _combatAction;

		Coroutine _activeTurn;

		// UI
		CombatStatUpdater _statUI;
		GameObject _targetArrow;

		Animator[] _damageNumbers;

		//combat
		Coroutine _enemyDamageAnimationCoroutine;

		// other
		Coroutine _actEffectCoroutine;
		Coroutine _pulseCoroutine;
		GameObject infoCanvas;
		private float accumulatedDamage;

		private bool damageProcessed;

		// bool doingDamage = false;   JERE: Not used?
		bool doingCameraDying;
		Material defaultMaterial;

		#endregion

		[Header("Only used on player chars give each player unique id they are used when adding to party")]
		public string characterID; // Or some other unique identifier

		private bool HasCounterAttack;
		public bool Alive = true;

		[HideInInspector] public List<SpellAction> spells; // These 2 are used for "new" enemy spells.
		public EnemyState state;
		private CombatCharacter[] _cachedActionTargets;
		private int _pendingHitEvents;
		private bool _finishEventSeen;
		private bool _hasPaidResourceCostThisTurn;
		[HideInInspector] public Material _baseMat;   // sprite’s real material – cached once
		private Material _blinkMat;  // reusable blink copy
		private SpriteRenderer _sr;
		private Color _originalColor;
		public RuntimeAnimatorController uiAnimator;
		#region Helpers and accessors
		private Coroutine _flickerCo;
		/// <summary>
		/// Current turn speed after calculating buffs.
		/// </summary>
		public float CurrentTurnSpeed
		{
			get
			{
				if (_stats == null)
				{
					Debug.LogError("Stats not initialized", this);
					return 0;
				}

				_stats[(int)StatType.Agility].ActBuffs(CountAsTurn.No);
				return _stats[(int)StatType.Agility].Value;
			}
		}

		public bool IsAlive
		{
			//float value can be 0.smth so floor down
			get
			{
				if (_stats == null)
				{
					Debug.LogError("Stats not initialized", this);
					return false;
				}
				return _stats[(int)StatType.Vitality].Value > 0;
			}
		}

		public int Index { get; set; }
		public int PlayerIndex { get; set; }

		public float CurrentTurnSpeedTEST;

		[SerializeField] private int extraTurns; // Visible in Inspector
		[HideInInspector] public int extraTurnsDefault;


		//Whenever you award extra turns (e.g., through a skill or ability), you simply modify the ExtraTurns field: currentCharacter.ExtraTurns += 2;
		// Find a way to reset extra turns to extra turns default value somewhere. In take damage method? or just use some sort of token?
		// handle edge cases such as NEED TO BE DONE:
		// Characters dying mid-turn.
		// Special actions that skip turns or reset turn orders.
		public int ExtraTurns
		{
			get { return extraTurns; }
			set { extraTurns = value; }
		}

		public void AddToParty()
		{
			PlayerPartyManager.Instance.InitNewPartyMember(this);
		}

		public void RemoveFromParty()
		{
			if (PlayerPartyManager.Instance.PlayerInstances.Contains(this))
			{
				PlayerPartyManager.Instance.RemoveFromParty(this);
			}
		}

		public override bool Equals(object obj)
		{
			if (obj == null || GetType() != obj.GetType())
			{
				return false;
			}

			CombatCharacter other = (CombatCharacter)obj;
			return characterID == other.characterID; // Compare based on unique ID or another property
		}

		public override int GetHashCode()
		{
			return characterID.GetHashCode(); // Ensure GetHashCode is consistent with Equals
		}

		public float GetCurrentStatValue(StatType stattype)
		{
			//_stats[(int)stattype].ActBuffs(CountAsTurn.No);
			return _stats[(int)stattype].Value;
		}

		public bool IsEnemyOrBoss()
		{
			return _info.characterClass is CharacterClass.Enemy or CharacterClass.Boss;
		}

		#region tokenEffects

		public bool IsVulnerable
		{
			get
			{
				foreach (Effect effect in CurrentEffects)
					if (effect.EffectType == EffectType.Vulnerable)
						return true;

				return false;
			}
		}

		public bool IsDefenceUP
		{
			get
			{
				foreach (Effect effect in CurrentEffects)
				{
					if (effect.EffectType == EffectType.DefenceUp)
						return true;
				}

				return false;
			}
		}

		public bool IsBlind
		{
			get
			{
				foreach (Effect effect in CurrentEffects)
				{
					if (effect.EffectType == EffectType.Blind)
						return true;
				}

				return false;
			}
		}

		public bool Dodge
		{
			get
			{
				foreach (Effect effect in CurrentEffects)
				{
					if (effect.EffectType == EffectType.Dodge)
						return true;
				}

				return false;
			}
		}

		public bool Burning
		{
			get
			{
				foreach (Effect effect in CurrentEffects)
				{
					if (effect.EffectType == EffectType.Burning)
						return true;
				}

				return false;
			}
		}

		public bool Bleeding
		{
			get
			{
				foreach (Effect effect in CurrentEffects)
				{
					if (effect.EffectType == EffectType.Bleeding)
						return true;
				}

				return false;
			}
		}

		public bool DmgIncrease
		{
			get
			{
				foreach (Effect effect in CurrentEffects)
				{
					if (effect.EffectType == EffectType.IncreasedDamage)
						return true;
				}

				return false;
			}
		}

		public bool Stunned
		{
			get
			{
				foreach (Effect effect in CurrentEffects)
				{
					if (effect.EffectType == EffectType.Stun)
						return true;
				}

				return false;
			}
		}

		public bool Weak
		{
			get
			{
				foreach (Effect effect in CurrentEffects)
				{
					if (effect.EffectType == EffectType.Weakness)
						return true;
				}

				return false;
			}
		}

		public bool Taunt
		{
			get
			{
				foreach (Effect effect in CurrentEffects)
				{
					if (effect.EffectType == EffectType.Taunt)
						return true;
				}

				return false;
			}
		}

		public bool CounterAttack
		{
			get
			{
				foreach (Effect effect in CurrentEffects)
				{
					if (effect.EffectType == EffectType.CounterAttack)
						return true;
				}

				return false;
			}
		}

		#endregion

		public Dictionary<EffectType, List<EffectType>> negationRules = new Dictionary<EffectType, List<EffectType>>
		{
			{ EffectType.DefenceUp, new List<EffectType> { EffectType.Vulnerable } }, // Example: First: Defence negates Second: Vulnerable
            { EffectType.Weakness, new List<EffectType> { EffectType.IncreasedDamage } },
			{ EffectType.Vulnerable, new List<EffectType> { EffectType.DefenceUp } },
			{ EffectType.IncreasedDamage, new List<EffectType> { EffectType.Weakness } },
			{ EffectType.Blind, new List<EffectType> { EffectType.Dodge } },
		};

		public CombatCharacterInfo Info
		{
			get { return _info; }
		}

		public CombatAction CombatAction
		{
			get { return _combatAction; }
		}

		public CombatStatUpdater CombatStatUpdater
		{
			get { return _statUI; }
			set { _statUI = value; }
		}

		public BaseStat[] Stats
		{
			get { return _stats; }
		}

		bool IsLowHP
		{
			get { return (_stats[(int)StatType.Vitality] as ChangingStat).Percentage <= _lowHP; }
		}

		bool IsMediumHP
		{
			get { return (_stats[(int)StatType.Vitality] as ChangingStat).Percentage <= _mediumHP; }
		}

		#endregion

		#region Targeting and highlighting

		public bool IsActiveCharacter
		{
			get => _isActiveCharacter;
			set
			{
				_isActiveCharacter = value;
				UpdateCameraGroupInclusion();
			}
		}

		public bool IsTargeted
		{
			get => _isTargeted;
			set
			{
				_isTargeted = value;
				SetTargeted(value);
				UpdateCameraGroupInclusion();
			}
		}

		private IEnumerable<Equipment> GetEquippedItems()
		{
			foreach (var slot in ItemSlots)
			{
				if (slot == null) continue;
				if (slot._currentItem is Equipment eq)
					yield return eq;
			}
		}

		public bool IsHighlighted
		{
			get => _isHighlighted;
			set
			{
				_isHighlighted = value;
				if (value)
				{
					// Highlight the character
				}
				// Remove the highlight
			}
		}

		private bool _isActiveCharacter;
		private bool _isTargeted;
		private bool _isHighlighted;

		private void UpdateCameraGroupInclusion()
		{
			bool isCombatParticipant = IsTargeted || IsAlive || IsActiveCharacter;
			bool isActionParticipant = IsActiveCharacter || IsTargeted;

			var mainGroup = CombatManager.Instance.CameraController.mainTargetGroup;
			var actionGroup = CombatManager.Instance.CameraController.closeUpTargetGroup;

			if (isCombatParticipant) CombatManager.Instance.CameraController.AddToTargetGroup(transform, mainGroup);
			else CombatManager.Instance.CameraController.RemoveFromTargetGroup(transform, mainGroup);

			if (isActionParticipant) CombatManager.Instance.CameraController.AddToTargetGroup(transform, actionGroup);
			else CombatManager.Instance.CameraController.RemoveFromTargetGroup(transform, actionGroup);
		}

		#endregion

		private void Update()
		{
			UpdateCameraGroupInclusion();
			HasCounterAttack = CounterAttack;
			CurrentTurnSpeedTEST = CurrentTurnSpeed;

			if (IsAlive)
			{
				if (CurrentEffects != null && characterClassForInspector == CharacterClassForInspector.Enemy)
				{
					CombatManager.Instance.tokenUi.SetTokenDisplay(Index, transform.gameObject);
				}
				else
				{
					CombatManager.Instance.tokenUi.SetTokenDisplayPlayer(PlayerIndex, transform.gameObject);
				}
			}

			// Character names to ui and their position.
			if (_info.characterClass == CharacterClass.Enemy || _info.characterClass == CharacterClass.Boss)
			{
				if (IsHighlighted)
				{
					if (CombatManager.Instance.combatnamesUI != null)
					{
						if (IsLowHP)
						{
							CombatManager.Instance.combatnamesUI.SetNameDisplay(Index, transform.gameObject,
								_info.characterName, Color.red);
						}
						else if (IsMediumHP)
						{
							CombatManager.Instance.combatnamesUI.SetNameDisplay(Index, transform.gameObject,
								_info.characterName, Color.yellow);
						}
						else
						{
							CombatManager.Instance.combatnamesUI.SetNameDisplay(Index, transform.gameObject,
								_info.characterName, Color.white);
						}
					}
				}
			}

			Alive = IsAlive;
		}


		public void Init(bool skipAnimatorExtension = false)
		{
			if (_info == null) Debug.LogError("Missing info", this);

			TurnsCompleted = 0;
			// init stats with max values of current level
			_stats = new BaseStat[Enum.GetValues(typeof(StatType)).Length];

			#region Error catching
			if (_info.StatData == null)
			{
				Debug.LogError($"[CombatCharacter.Init] StatData is null on {_info.characterName}", this);
				_info.StatData = new List<float>();
			}

			int expectedCount = System.Enum.GetValues(typeof(StatType)).Length * 3;
			while (_info.StatData.Count < expectedCount)
			{
				_info.StatData.Add(0f);
			}
			#endregion

			for (int i = 0; i < _stats.Length; i++)
			{
				var statType = (StatType)i;
				int statValue = _info.GetFinalStat(statType);

				switch (statType)
				{
					case StatType.Vitality:
					case StatType.Mana:
						_stats[i] = new ChangingStat(statValue);
						break;

					case StatType.Agility:
					case StatType.Strength:
					case StatType.Magic:
					case StatType.Luck:
					case StatType.Defence:
						_stats[i] = new StaticStat(statValue);
						break;

					default:
						Debug.LogWarning($"Unhandled stat type: {statType}");
						break;
				}
			}

			spriteAnimator = GetComponentInChildren<AnimatorExtension>();
			_targetArrow = transform.GetChild(TARGET_ARROW_INDEX).gameObject;
			_damageNumbers =
				transform.GetChild(DAMAGE_NUMBER_INDEX)
					.GetComponentsInChildren<Animator>(); // This will be in UI in the future?

			// setup player character
			if (_info.characterClass < CharacterClass.Enemy)
			{
				//Set stats to available info
				if (PlayerPartyManager.Instance)
				{
					foreach (CombatCharacter cc in PlayerPartyManager.Instance.PlayerInstances)
					{
						if (name.Contains(cc.name))
						{
							// saved current values
							_stats[(int)StatType.Vitality].SetStat(cc.Stats[(int)StatType.Vitality].Value);
							_stats[(int)StatType.Mana].SetStat(cc.Stats[(int)StatType.Mana].Value);
							_stats[(int)StatType.Agility].SetStat(cc.Stats[(int)StatType.Agility].Value);
							_stats[(int)StatType.Strength].SetStat(cc.Stats[(int)StatType.Strength].Value);
							_stats[(int)StatType.Magic].SetStat(cc.Stats[(int)StatType.Magic].Value);
							_stats[(int)StatType.Luck].SetStat(cc.Stats[(int)StatType.Luck].Value);
							_stats[(int)StatType.Defence].SetStat(cc.Stats[(int)StatType.Defence].Value);

							if (skipAnimatorExtension) break;

							spriteAnimator.ClearValues(this);
							spriteAnimator.SetBool(AnimatorExtension.ANIMATOR_IS_MOVING_TO_COMBAT, true);
							spriteAnimator.SetBool(AnimatorExtension.ANIMATOR_IS_DEAD, !IsAlive);
							spriteAnimator.SetBool(AnimatorExtension.ANIMATOR_IS_LOWHP, IsLowHP);
							break;
						}
					}
				}

				InitEquipment();

			}
			else // setup enemy character starting token
			{
				if (StartingTokens != null /*|| TempStartingTokens != null*/)
				{
					StartCoroutine(StartToken(false));
				}

				if (_info.spells != null)
				{
					state = new EnemyState(_info.spells); // Setting up the state if enemy or boss char.
				}

				defaultMaterial = transform.GetComponentInChildren<SpriteRenderer>().material;

			}

			_sr = GetComponentInChildren<SpriteRenderer>();
			if (_sr != null) _originalColor = _sr.color;

			TotalSpeed = 0;
			TurnsStartedPreview = 0;
			TurnsStarted = 0;
			extraTurnsDefault = extraTurns;
		}

		public void BeginAction(CombatAction action, int expectedHits)
		{
			_pendingHitEvents = expectedHits;
			_finishEventSeen = false;
			_combatAction = action;
		}

		public void OnHitAppliedFromAnim()   // call this from each PlayHitVFX event
		{
			_pendingHitEvents = Mathf.Max(0, _pendingHitEvents - 1);
			Debug.Log($"[HitApplied] pending={_pendingHitEvents}");
		}

		public void OnActionFinishedFromAnim()   // call this from a final “EndAction” anim event
		{
			_finishEventSeen = true;
			Debug.Log("Action finished called from anim.");
		}
		public bool ActionReallyDone => _finishEventSeen && _pendingHitEvents == 0;
		public IEnumerator StartToken(bool isPlayer)
		{
			var ui = CombatManager.Instance.tokenUi;
			if (!ui) { Debug.LogError("TokenUI is not added to CombatManager gameobject."); yield break; }

			// Wait until the UI data structures exist
			while (!ui.IsReady) yield return null;

			// Also wait until lists have correct counts
			if (!isPlayer)
				while (ui.EffectsForEachEnemy == null || ui.EffectsForEachEnemy.Count != ui.EnemyPanels.Count) yield return null;
			else
				while (ui.EffectsForEachPlayer == null || ui.EffectsForEachPlayer.Count != ui.PlayerPanels.Count) yield return null;

			// Let layout/animators bind
			yield return null;
			yield return new WaitForEndOfFrame();

			var src = isPlayer ? TempStartingTokensOnPlayers : StartingTokens;
			if (src != null)
			{
				foreach (var ev in src)
					AddEffect(new Effect(ev));           // this triggers the add animation per token
			}

			// Give the just-created token animators a frame to appear
			yield return null;

			// Safety pin so we never end at alpha=0 due to an interrupted intro
			ui.NormalizeTokenAlpha(isPlayer ? PlayerIndex : Index, isPlayer);

			if (isPlayer) TempStartingTokensOnPlayers = null;
		}

		public void DeActivateTargetingArrow()
		{
			_targetArrow.SetActive(false);
		}

		private void SetTargeted(bool isTargeted)
		{
			// Dont do targeting stuff if is dead.
			if (!IsAlive) return;

			_targetArrow.SetActive(isTargeted);

			if (isTargeted)
			{
				if (_pulseCoroutine == null && gameObject.activeInHierarchy)
				{
					_pulseCoroutine = StartCoroutine(TargetBlink());
				}
			}
			else
			{
				if (_pulseCoroutine != null)
				{
					StopCoroutine(_pulseCoroutine);
					transform.GetComponentInChildren<SpriteRenderer>().material
						.SetFloat("_Metallic",
							0.0f); //TODO: Change this when know a way to change it to be more "brighter" currently "darkens"
					_pulseCoroutine = null;
				}
			}

			if (CurrentTurnOrderIcon)
			{
				CurrentTurnOrderIcon.animator.SetLayerWeight((int)LayerID.Target, isTargeted ? 1f : 0f);
			}

			if (NextTurnOrderIcon)
			{
				NextTurnOrderIcon.animator.SetLayerWeight((int)LayerID.Target, isTargeted ? 1f : 0f);
			}
		}

		#region Changing stats

		public void ReviveCharacter(float hpAmount)
		{
			ChangeChangingStatValue(hpAmount);
			if (IsAlive)
			{
				spriteAnimator.ClearValues(this);
				spriteAnimator.SetBool(AnimatorExtension.ANIMATOR_IS_LOWHP, IsLowHP);
				spriteAnimator.SetBool(AnimatorExtension.ANIMATOR_IS_DEAD, !IsAlive);

				CombatManager.Instance.CharacterRevived(this);
			}
		}


		public void ChangeChangingStatValue(float amount, ShowDamageText showDamage = ShowDamageText.No, StatType statType = StatType.Vitality,
			CharacterClass attacker = 0, CombatCharacter attackerScript = null,
			EffectType effectType = EffectType.None,
			CombatAction combatAction = null)
		{
			if (!IsAlive)
			{
				HideIcons();
				return;
			}

			// Apply effects that modify the amount
			List<EffectType> consumedDefences;
			amount = ApplyDamageModifiers(amount, statType, effectType, out consumedDefences);

			// Handle CounterAttack effect
			HandleCounterAttack(attackerScript, effectType);

			// Remove expired effects
			ConsumeDefenceHits(consumedDefences, attacker, effectType);

			RemoveExpiredTurnsOnly(effectType);

			// Apply the stat change
			ApplyStatChange(amount, statType, attacker, attackerScript, combatAction, effectType, showDamage);

			// Update UI
			_statUI?.UpdateStatUI(_stats);
		}

		private void HideIcons()
		{
			if (IsEnemyOrBoss())
				CombatManager.Instance.tokenUi.HideIcons(Index);
			else
				CombatManager.Instance.tokenUi.HideIconsPlayer(PlayerIndex);
		}

		private float ApplyDamageModifiers(
	float amount,
	StatType statType,
	EffectType effectType,
	out List<EffectType> consumedDefences)
		{
			consumedDefences = new List<EffectType>();


			if (statType != StatType.Vitality || IsDotEffect(effectType))
				return amount;



			// Vulnerable (double damage)
			if (IsVulnerable)
			{
				amount *= 2f;
				consumedDefences.Add(EffectType.Vulnerable);
				Debug.Log("Vulnerable effect: 2x damage", this);
			}

			// DefenceUp (half damage)
			if (IsDefenceUP)
			{
				amount *= 0.5f; // your previous /2
				consumedDefences.Add(EffectType.DefenceUp);
				Debug.Log("DefenceUp effect: Damage reduced", this);
			}

			// Dodge (negates the hit)
			if (Dodge)
			{
				amount = 0f;
				consumedDefences.Add(EffectType.Dodge);
				Debug.Log("Dodge effect: Attack dodged", this);
			}

			// >>> EXTENSION POINT:
			// If you add new defence-like effects that directly change the hit number,
			// do their math here AND add their EffectType to consumedDefences when they apply.
			//
			// Example:
			// if (HasThorns) { /* thorns doesn't change incoming number, so don't add here, would add it like counter attack so it does damage to attacker */ }
			// if (HasShieldX) { amount -= shieldValue; if (shieldValue > 0) consumedDefences.Add(EffectType.ShieldX); }

			return amount;
		}

		private bool IsDotEffect(EffectType effectType)
		{
			return effectType == EffectType.Bleeding || effectType == EffectType.Burning ||
				   effectType == EffectType.Poison;
		}

		private void DecreaseEffectHits(EffectType effectType)
		{
			Effect effect = CurrentEffects.FirstOrDefault(e => e.EffectType == effectType);
			if (effect != null && effect.hitsActive > 0)
			{
				effect.hitsActive--;
				if (effect.hitsActive == 0)
				{
					Debug.Log($"Removing effect: {effectType}", this);
					CurrentEffects.Remove(effect);
					UpdateEffectsUI();
				}
			}
		}

		private void HandleCounterAttack(CombatCharacter attackerScript, EffectType effectType)
		{
			if (CounterAttack && attackerScript != null && !IsDotEffect(effectType))
			{
				Debug.Log($"Counter-attacking {_info.characterName}", this);
				attackerScript.ChangeChangingStatValue(_info.basicAttack.directDamage, ShowDamageText.Yes, StatType.Vitality,
					_info.characterClass, this);

				// Play anim here of counter attacking.
				spriteAnimator.SetBoolForSeconds(AnimatorExtension.ANIMATOR_IS_USINGABILITY3, true,
						spriteAnimator.attackDuration);

				DecreaseEffectHits(EffectType.CounterAttack);
			}
		}

		private void ConsumeDefenceHits(List<EffectType> consumed, CharacterClass attacker, EffectType effectType)
		{
			if (consumed == null || consumed.Count == 0) return;
			if (IsDotEffect(effectType)) return;                       // DoTs shouldn't consume defence hits here
			if (attacker == _info.characterClass) return;              // self-targeting (buffs, self-heals) don't consume

			List<Effect> used = new();

			foreach (var e in CurrentEffects.ToArray())
			{
				if (e.hitsActive <= 0) continue;
				if (e.EffectCategory != EffectCategories.Defence) continue;
				if (!consumed.Contains(e.EffectType)) continue;        // consume only the defence that actually applied

				e.hitsActive--;
				if (e.hitsPerStack > 0)
				{
					int expectedStacks = Mathf.CeilToInt((float)e.hitsActive / e.hitsPerStack);
					e.stacksCount = Mathf.Min(e.stacksCount, expectedStacks);
				}
				if (e.hitsActive == 0) used.Add(e);
			}

			foreach (var e in used)
			{
				if (IsEnemyOrBoss())
					CombatManager.Instance.tokenUi.UpdateEffectsForEnemy(Index, CurrentEffects, false, e);
				else
					CombatManager.Instance.tokenUi.UpdateEffectsForPlayer(PlayerIndex, CurrentEffects, false, e);

				StartCoroutine(RemoveEffectFromList(e));
			}
		}

		private void RemoveExpiredTurnsOnly(EffectType effectType)
		{
			// Only handle turn-based expiry here (clean separation of concerns)
			List<Effect> toRemove = new();
			foreach (var effect in CurrentEffects.ToArray())
			{
				if (effect.affectedByTurns != AffectedByTurns.Yes) continue;

				effect.turnsActive--;
				if (effect.turnsPerStack > 0)
				{
					int expectedStacks = Mathf.CeilToInt((float)effect.turnsActive / effect.turnsPerStack);
					effect.stacksCount = Mathf.Min(effect.stacksCount, expectedStacks);
				}
				if (effect.turnsActive <= 0) toRemove.Add(effect);
			}

			foreach (var e in toRemove)
			{
				if (e.EffectType == EffectType.Stun && StunEffect != null)
				{
					Destroy(StunEffect);
					StunEffect = null;
				}

				if (IsEnemyOrBoss())
					CombatManager.Instance.tokenUi.UpdateEffectsForEnemy(Index, CurrentEffects, false, e);
				else
					CombatManager.Instance.tokenUi.UpdateEffectsForPlayer(PlayerIndex, CurrentEffects, false, e);

				StartCoroutine(RemoveEffectFromList(e));
			}
		}

		private void UpdateEffectsUI()
		{
			if (IsEnemyOrBoss())
				CombatManager.Instance.tokenUi.UpdateEffectsForEnemy(Index, CurrentEffects);
			else
				CombatManager.Instance.tokenUi.UpdateEffectsForPlayer(PlayerIndex, CurrentEffects);
		}

		private void ApplyStatChange(float amount, StatType statType, CharacterClass attacker,
			CombatCharacter attackerScript, CombatAction combatAction, EffectType effectType, ShowDamageText showDamageText)
		{
			switch (statType)
			{
				case StatType.Vitality:
					ApplyHealthChange(amount, attacker, attackerScript, combatAction, effectType, showDamageText);
					break;
				case StatType.Mana:
					(_stats[(int)StatType.Mana] as ChangingStat).AddDirectly(-amount);
					break;
				default:
					Debug.LogWarning("StatType is not Health, Mana, or Combo!");
					break;
			}
		}

		private void ApplyHealthChange(float amount, CharacterClass attacker, CombatCharacter attackerScript,
			CombatAction combatAction, EffectType effectType, ShowDamageText showDamageText)
		{
			// If no action provided, just apply the raw amount and exit
			if (combatAction == null)
			{
				(_stats[(int)StatType.Vitality] as ChangingStat).AddDirectly(amount);

				Debug.Log("Direct unscaled amount applied: " + amount, this);

				if (amount < 0)
				{
					DisplayDamage(amount, effectType, showDamageText, false);
					PlayDamageAnimations(effectType);
					SetDamageAnimation(attacker, attackerScript, combatAction, effectType);
				}
				else if (amount == 0)
				{
					DisplayMiss(attackerScript, showDamageText);
				}
				return;
			}

			// Continue with scaling, defence, crit etc 
			if (amount != 0)
			{
				StatType scalingStat = combatAction.statTypeToAffectSkill;
				float scalingValue = attackerScript.GetCurrentStatValue(scalingStat);
				float multiplier = combatAction.scalingMultiplier;
				float penetration = combatAction.armourPenetration;

				if (amount < 0)
				{
					amount -= scalingValue * multiplier;

					float defence = GetCurrentStatValue(StatType.Defence);
					float effectiveDef = defence * (1f - penetration);
					float reduction = Mathf.Clamp(effectiveDef / 100f, 0f, 0.9f); // 90% max reduction
					amount *= 1f - reduction;

					if (amount > 0)
						amount = 0;
				}
				else
				{
					amount += scalingValue * multiplier;
				}
				Debug.Log("Scaled damage: " + amount, this);
			}

			// Critical check
			bool isCritical = false;
			if (attackerScript != null)
			{
				float critChance = attackerScript.GetCurrentStatValue(StatType.Luck); // 1 point of Luck is 1% Crit Chance
				if (UnityEngine.Random.value < (critChance / 100f))
				{
					isCritical = true;
					amount *= 1.5f;
				}
			}

			(_stats[(int)StatType.Vitality] as ChangingStat).AddDirectly(amount);
			Debug.Log($"[DamageApplied] with {combatAction.actionName} to {name} amount={amount} isCrit={isCritical} " +
		  $"showFlag={showDamageText} dmgUI={(CombatManager.Instance.damageNumbersUI != null)}");
			Debug.Log("Damage is: " + amount, this);

			if (amount < 0)
			{
				DisplayDamage(amount, effectType, showDamageText, isCritical);
				PlayDamageAnimations(effectType);
				SetDamageAnimation(attacker, attackerScript, combatAction, effectType);
			}
			else if (amount == 0)
			{
				DisplayMiss(attackerScript, showDamageText);
			}
		}

		private void DisplayDamage(float amount, EffectType effectType, ShowDamageText showDamageText, bool isCritical = false)
		{
			int damageAmount = Mathf.RoundToInt(Mathf.Abs(amount));
			Color damageColor = isCritical
		? CombatManager.Instance.CriticalHitColor
		: GetDamageColor();

			if (CombatManager.Instance.damageNumbersUI != null && showDamageText == ShowDamageText.Yes)
			{
				if (IsEnemyOrBoss())
					CombatManager.Instance.damageNumbersUI.ShowDamageNumberForEnemy(Index, gameObject, damageAmount, damageColor, isCritical);
				else
					CombatManager.Instance.damageNumbersUI.ShowDamageNumberForPlayer(PlayerIndex, gameObject, damageAmount, damageColor, isCritical);
			}
		}

		public void RefreshEquipmentStats()
		{
			ApplyEquipmentStats();
		}

		private void DisplayMiss(CombatCharacter attackerScript, ShowDamageText showDamageText)
		{
			if (attackerScript != null && CombatManager.Instance.damageNumbersUI != null && showDamageText == ShowDamageText.Yes)
			{
				Color damageColor = GetDamageColor();
				if (IsEnemyOrBoss())
					CombatManager.Instance.damageNumbersUI.ShowDamageNumberForEnemy(Index, gameObject, 0, damageColor);
				else
					CombatManager.Instance.damageNumbersUI.ShowDamageNumberForPlayer(PlayerIndex, gameObject, 0,
						damageColor);
			}
		}

		private Color GetDamageColor()
		{
			if (IsDefenceUP || Dodge)
				return CombatManager.Instance.ReducedDamageColor;
			else if (IsVulnerable)
				return CombatManager.Instance.IncreasedDamageColor;
			else
				return CombatManager.Instance.NormalColor;
		}

		private void PlayDamageAnimations(EffectType effectType)
		{
			if (CurrentTurnOrderIcon)
			{
				CurrentTurnOrderIcon.animator.SetLayerWeight((int)LayerID.Target, 0f);
				CurrentTurnOrderIcon.animator.SetLayerWeight((int)LayerID.Health, 1f);
				CurrentTurnOrderIcon.animator.Play(
					IsAlive ? StateName.Damaged.ToString() : StateName.Dead.ToString(), (int)LayerID.Health, 0f);
				CurrentTurnOrderIcon.dead = !IsAlive;
			}

			if (NextTurnOrderIcon)
			{
				NextTurnOrderIcon.animator.SetLayerWeight((int)LayerID.Target, 0f);
				NextTurnOrderIcon.animator.SetLayerWeight((int)LayerID.Health, 1f);
				NextTurnOrderIcon.animator.Play(
					IsAlive ? StateName.Damaged.ToString() : StateName.Dead.ToString(), (int)LayerID.Health, 0f);
				NextTurnOrderIcon.dead = !IsAlive;
			}
		}


		public List<ChangingStat> GetChangingStats()
		{
			List<ChangingStat> changingStats = new();
			for (int i = 0; i < (int)StatType.Agility; i++)
			{
				changingStats.Add(_stats[i] as ChangingStat);
			}

			return changingStats;
		}

		public void SetChangingStatsInfo(List<ChangingStat> changingStats)
		{
			for (int i = 0; i < changingStats.Count; i++)
			{
				(_stats[i] as ChangingStat).AddDirectly(-(_stats[i].Value - changingStats[i].Value));
			}
		}

		#endregion

		#region Buffs and Effects

		public void AddBuff(Buff buff)
		{
			//_stats[(int)buff.statToBuff].AddBuff(buff);
		}

		public void AddEffect(Effect effect)
		{
			if (effect.turnsActive == 0)
				return;

			bool effectExists = false;
			// Stacks tokens
			foreach (Effect exsistingEffect in CurrentEffects)
			{
				if (exsistingEffect.EffectType == effect.EffectType && (effect.EffectType != EffectType.Bleeding ||
																		effect.EffectType != EffectType.Burning ||
																		effect.EffectType != EffectType.Poison))
				{
					exsistingEffect.turnsActive += effect.turnsActive;
					exsistingEffect.hitsActive += effect.hitsActive;
					exsistingEffect.value += effect.value;
					exsistingEffect.stacksCount += 1;

					exsistingEffect.hitsPerStack = effect.hitsActive / exsistingEffect.stacksCount;
					exsistingEffect.turnsPerStack = effect.turnsActive / exsistingEffect.stacksCount;
					effectExists = true;
					break;
				}
			}

			if (!effectExists)
			{
				CurrentEffects.Add(effect);

				if (effect.EffectType == EffectType.Stun)
				{
					Vector3 spawnPosition = gameObject.transform.position + stunOffset;
					StunEffect = Instantiate(VFXPrefabHolder.StunEffect, spawnPosition, Quaternion.identity);
				}

				if (_info.characterClass == CharacterClass.Enemy || _info.characterClass == CharacterClass.Boss)
				{
					Debug.Log("Updating show token");
					CombatManager.Instance.tokenUi.UpdateEffectsForEnemy(Index, CurrentEffects,
						true); // This "activates" token animation
					if (CombatManager.Instance.tokenUi.enabled == true)
						CombatManager.Instance.tokenUi.ShowAddedToken(effect.EffectIcon, CombatManager.Instance.tokenUi.EnemyAddedPanels[Index], 0, Index, false, effect);
				}
				else
				{
					CombatManager.Instance.tokenUi.UpdateEffectsForPlayer(PlayerIndex, CurrentEffects,
						true); // This "activates" token animation
					if (CombatManager.Instance.tokenUi.enabled == true)
						CombatManager.Instance.tokenUi.ShowAddedToken(effect.EffectIcon, CombatManager.Instance.tokenUi.PlayerAddedPanels[PlayerIndex], PlayerIndex, 0, true, effect);
				}
			}
			else
			{
				if (_info.characterClass == CharacterClass.Enemy || _info.characterClass == CharacterClass.Boss)
				{
					Debug.Log("Updating show exitinging token");
					CombatManager.Instance.tokenUi.UpdateEffectsForEnemy(Index, CurrentEffects,
						false);

					CombatManager.Instance.tokenUi.ShowAddedToken(effect.EffectIcon, CombatManager.Instance.tokenUi.EnemyAddedPanels[Index], 0, Index, false, effect);
				}
				else
				{
					CombatManager.Instance.tokenUi.UpdateEffectsForPlayer(PlayerIndex, CurrentEffects,
						false);

					CombatManager.Instance.tokenUi.ShowAddedToken(effect.EffectIcon, CombatManager.Instance.tokenUi.PlayerAddedPanels[PlayerIndex], PlayerIndex, 0, true, effect);
				}
			}


			//CurrentEffects.Add(effect);
			NegateEffects(effect);
		}

		public void NegateEffects(Effect effect, bool addEffect = false) // also just decrease stack by one dont destroy if stacks more than 1 ?
		{
			if (negationRules.ContainsKey(effect.EffectType))
			{
				List<EffectType> negates = negationRules[effect.EffectType];
				for (int i = CurrentEffects.Count - 1; i >= 0; i--)
				{
					Effect existingEffect = CurrentEffects[i];
					if (negates.Contains(existingEffect.EffectType))
					{
						Debug.Log("Removing older effect with negate: " + existingEffect.EffectType, this);

						#region Negating vfx

						if (existingEffect.EffectType == EffectType.Stun)
						{
							if (StunEffect != null)
							{
								Destroy(StunEffect);
								StunEffect = null;
							}
						}

						if (existingEffect.EffectType == EffectType.DefenceUp)
						{
							Vector3 position = gameObject.transform.position;
							position.y = 4; // Check this with all enemies.
							if (_info.characterClass == CharacterClass.Enemy ||
								_info.characterClass == CharacterClass.Boss)
								position.z = -3;
							else
								position.z = 3;
							Instantiate(VFXPrefabHolder.breakDefenceEffect, position,
								VFXPrefabHolder.breakDefenceEffect.transform.rotation);
						}

						#endregion

						if (_info.characterClass == CharacterClass.Enemy || _info.characterClass == CharacterClass.Boss)
						{
							Debug.Log("Updating show exitinging token negate");
							CombatManager.Instance.tokenUi.UpdateEffectsForEnemy(Index, CurrentEffects, false, existingEffect);
						}
						else
						{
							CombatManager.Instance.tokenUi.UpdateEffectsForPlayer(PlayerIndex, CurrentEffects, false, existingEffect);
						}


						if (addEffect)
							StartCoroutine(AddEffectFromNegate(existingEffect, effect, true));
						else
							StartCoroutine(AddEffectFromNegate(existingEffect, effect, false));
					}
				}
			}
		}



		IEnumerator AddEffectFromNegate(Effect effectToRemove, Effect effectToAdd, bool add)
		{
			yield return new WaitForSeconds(0.5f);

			if (CurrentEffects.Contains(effectToRemove))
			{
				CurrentEffects.Remove(effectToRemove);
			}

			if (add)
				AddEffect(effectToAdd);

			UpdateEffectsUI(); // TESTING
		}

		IEnumerator RemoveEffectFromList(Effect effect = null)
		{
			yield return new WaitForSeconds(0.5f);
			if (effect != null)
				CurrentEffects.Remove(effect);

			if (_info.characterClass == CharacterClass.Enemy || _info.characterClass == CharacterClass.Boss)
			{
				Debug.Log("Updating show exitinging token removeeffectfromlist");
				CombatManager.Instance.tokenUi.UpdateEffectsForEnemy(Index, CurrentEffects, false);
			}
			else
			{
				CombatManager.Instance.tokenUi.UpdateEffectsForPlayer(PlayerIndex, CurrentEffects, false);
			}
		}

		/// <summary>
		/// Apply current effects to the character
		/// </summary>
		/// <returns></returns>
		IEnumerator ActEffects()
		{
			if (damageProcessed)
			{
				yield break;
			}

			damageProcessed = true;

			List<Effect> usedEffects = new();
			EffectType mostRecentDotEffectType = EffectType.None;
			foreach (Effect effect in CurrentEffects.ToArray())
			{
				if (effect.affectedByTurns == AffectedByTurns.Yes)
					effect.turnsActive--;
				if (effect.turnsPerStack > 0)
				{
					int expectedStacks = Mathf.CeilToInt((float)effect.turnsActive / effect.turnsPerStack);
					if (expectedStacks < effect.stacksCount)
					{
						effect.stacksCount = expectedStacks;
					}
				}
				if (effect.EffectType != EffectType.Vulnerable && effect.EffectType != EffectType.Dodge && effect.EffectType != EffectType.DefenceUp && effect.EffectType != EffectType.Weakness)
				{
					if (effect.EffectType == EffectType.Bleeding || effect.EffectType == EffectType.Burning ||
						effect.EffectType == EffectType.Poison)
					{
						accumulatedDamage += effect.value;
						mostRecentDotEffectType = effect.EffectType;
					}
					else
					{
						Debug.Log("Taking damage from else statement of acteffects", this);
						ChangeChangingStatValue(effect.value, effect.ShowdamageText, effect.StatToEffect);
						yield return new WaitForSeconds(0.1f);
					}
				}

				if (effect.turnsActive <= 0 &&
					effect.affectedByTurns ==
					AffectedByTurns.Yes)
					usedEffects.Add(effect);
			}

			foreach (Effect effect in usedEffects)
			{
				Debug.Log("Removing effect", this);
				if (effect.EffectType == EffectType.Stun)
				{
					if (StunEffect != null)
					{
						Destroy(StunEffect);
						StunEffect = null;
					}
				}

				if (_info.characterClass == CharacterClass.Enemy || _info.characterClass == CharacterClass.Boss)
				{
					Debug.Log("Updating show exitinging token acteffects");
					CombatManager.Instance.tokenUi.UpdateEffectsForEnemy(Index, CurrentEffects, false, effect);
					Debug.LogWarning("TODO: Check these more thorougly.");
				}
				else
				{
					CombatManager.Instance.tokenUi.UpdateEffectsForPlayer(PlayerIndex, CurrentEffects, false, effect);
				}

				StartCoroutine(RemoveEffectFromList(effect));
			}

			usedEffects.Clear();

			// Apply accumulated damage
			if (accumulatedDamage != 0)
			{
				Debug.Log("Applying Accumalated damage amount: " + accumulatedDamage, this);
				ChangeChangingStatValue(accumulatedDamage, ShowDamageText.Yes,
					effectType: mostRecentDotEffectType);

				if (_info.characterClass == CharacterClass.Enemy || _info.characterClass == CharacterClass.Boss)
				{
					CombatManager.Instance.tokenUi.PlayDotAnim(enemyIndex: Index, isPlayer: false);
				}
				else
				{
					CombatManager.Instance.tokenUi.PlayDotAnim(playerIndex: PlayerIndex, isPlayer: true);
				}
			}

			accumulatedDamage = 0f;
			damageProcessed = false;
			if (_actEffectCoroutine == null)
			{
				StopCoroutine(ActEffects());
				yield break;
			}

			StopCoroutine(_actEffectCoroutine);
		}

		public void AddPreviewBuffs(List<Buff> previewBuffs, StatType statType)
		{
			for (int i = 0; i < previewBuffs.Count;)
			{
				if (previewBuffs[i].statToBuff != statType)
					previewBuffs.Remove(previewBuffs[i]);
				else
					i++;
			}

			previewBuffs.TrimExcess();
			_stats[(int)statType].AddPreviewBuffs(previewBuffs);
		}

		public void RemovePreviewBuffs(StatType statType)
		{
			_stats[(int)statType].RemovePreviewBuffs();
		}

		#endregion

		#region Turn logic

		public float GetTurnSpeedNTurnsAhead(int turnsAhead)
		{
			(_stats[(int)StatType.Agility] as StaticStat).ActBuffsNTurnsAhead(turnsAhead);
			return _stats[(int)StatType.Agility].Value;
		}

		public float GetTotalSpeedAtTurnsStarted()
		{
			return turnsStartedTotalSpeed.GetValueOrDefault(TurnsStarted, 0f);
		}

		public void StartTurn()
		{
			EnsureDebugEquipmentEquipped();
			_hasPaidResourceCostThisTurn = false;

			if (CurrentEffects.Count > 0)
			{
				_actEffectCoroutine = StartCoroutine(ActEffects());

				if (_info.characterClass is CharacterClass.Enemy or CharacterClass.Boss)
					CombatManager.Instance.tokenUi.UpdateEffectsForEnemy(Index, CurrentEffects);

				else
					CombatManager.Instance.tokenUi.UpdateEffectsForPlayer(PlayerIndex, CurrentEffects);
			}

			foreach (BaseStat stat in _stats)
				stat.ActBuffs(CountAsTurn.Yes);

			TurnsStarted++;

			_activeTurn = CombatManager.Instance.StartCoroutine(_info.characterClass < CharacterClass.Enemy
				? CombatManager.Instance.PlayerTurnLogic()
				: CombatManager.Instance.EnemyTurnLogic());

			startingTurn = true;
		}


		public void HighlightStatUI()
		{
			_statUI.UpdateStatUI(_stats);
			_statUI.SetHighlight(true);
		}

		public void SetAction(CombatAction action = null)
		{
			_cachedActionTargets = null;
			if (!IsAlive)
			{
				return;
			}

			// random action for enemy
			if (_info.characterClass >= CharacterClass.Enemy)
			{
				int totalWeight = basicAttackWeight + spellAttackWeight;

				int randomValue = Random.Range(0, totalWeight);
				if (randomValue < basicAttackWeight)
				{
					_combatAction =
						_info.basicAttack;
					CombatManager.Instance.StartTargeting(_info.basicAttack);
					Debug.Log(
						$"Random value ({randomValue}) fell below the basic attack weight ({basicAttackWeight}), selecting basic attack instead of a spell.",
						this);
				}
				else
				{
					Debug.Log("Random action on enemy");

					SpellAction selectedSpell = ChooseSpellToCast(this);

					if (selectedSpell == null)
					{
						_combatAction =
							_info.basicAttack;
						CombatManager.Instance.StartTargeting(_info.basicAttack);
						Debug.Log("Random No spell, all spells on cooldown", this);
					}
					else
					{
						_combatAction =
							selectedSpell;
						CastSpell(selectedSpell);
						CombatManager.Instance.StartTargeting(selectedSpell);
						Debug.Log(
							"Random Casting spell: " + selectedSpell.actionName + " : Cooldown is = " +
							selectedSpell.remainingCoolDown, this);
					}
				}

				StartFlicker();
			}
			else
			{
				for (int i = 0; i < CombatManager.EnemyInstances.Length; i++)
				{
					if (CombatManager.EnemyInstances[i].Taunt && CombatManager.EnemyInstances[i].IsAlive)
					{
						_combatAction = _info.basicAttack;
						CombatManager.Instance._enemyTargetIndex =
							CombatManager.Instance.UpdateSingleTarget(CombatManager.EnemyInstances, i, 0);
						Debug.Log("Player attack taunt enemy");
						break;
					}

					if (action == null)
					{
						_combatAction = _info.basicAttack;
						Debug.Log("[Combat] action was null");
						CombatManager.Instance.StartTargeting(_combatAction);
						break;
					}

					_combatAction = action;
					CombatManager.Instance.StartTargeting(_combatAction);
					break;
				}
			}
		}

		#region Enemies Spell system

		private void CastSpell(SpellAction spell)
		{
			if (spell == null || state == null) return;

			if (state.CanCastSpell(spell))
			{
				state.CastSpell(spell);
			}
		}

		private static void LogEnemyCooldowns(EnemyState enemyState)
		{
			if (enemyState != null)
			{
				List<string> cooldownInfo = enemyState.GetCooldownInfo();

				foreach (string info in cooldownInfo)
				{
					Debug.Log(info);
				}
			}
		}

		private SpellAction ChooseSpellToCast(CombatCharacter enemy)
		{
			if (enemy.state != null)
				LogEnemyCooldowns(enemy.state);

			foreach (var spell in _info.spells)
			{
				if (enemy.state == null) continue;
				if (enemy.state.CanCastSpell(spell))
				{
					return spell;
				}
			}

			return null;
		}

		#endregion

		public void ActivateInfo()
		{
			if (!IsAlive) return;

			if (CombatManager.Instance.combatnamesUI != null)
			{
				IsHighlighted = true;
			}
		}

		public void DeactivateInfo()
		{
			if (CombatManager.Instance.combatnamesUI == null) return;
			if (IsEnemyOrBoss())
			{
				CombatManager.Instance.combatnamesUI.SetNameDisplay(Index, transform.gameObject, "", Color.white);
			}

			IsHighlighted = false;
		}

		private IEnumerator TargetBlink()
		{
			const float pulseSpeed = 2.0f;
			const float minMetallic = 0.0f;
			const float maxMetallic = 1.0f;


			while (true)
			{
				float metallic = Mathf.PingPong(Time.time * pulseSpeed, maxMetallic - minMetallic) + minMetallic;
				if (transform.GetComponentInChildren<SpriteRenderer>().material == defaultMaterial)
				{
					defaultMaterial.SetFloat("_Metallic",
						metallic);
				}

				yield return null;
			}
		}


		private IEnumerator Flicker()
		{
			if (_sr == null) yield break;

			yield return new WaitForSeconds(_info.animationAct / 2f);

			const int blinkCount = 2;
			const float blinkDuration = 0.02f;

			for (int i = 0; i < blinkCount; i++)
			{
				_sr.color = new Color(0.9f, 0.9f, 0.9f, _originalColor.a);
				yield return new WaitForSeconds(blinkDuration);
				_sr.color = _originalColor;
				yield return new WaitForSeconds(blinkDuration);
			}

			_sr.color = _originalColor;
			_flickerCo = null;
		}

		private void StartFlicker()
		{
			if (!gameObject.activeInHierarchy) return;
			if (_flickerCo != null) StopCoroutine(_flickerCo);
			_flickerCo = StartCoroutine(Flicker());
		}

		public void StopFlicker()
		{
			if (_flickerCo != null)
			{
				StopCoroutine(_flickerCo);
				_flickerCo = null;

				var sr = GetComponentInChildren<SpriteRenderer>();
				if (sr != null) sr.material.color = Color.white;
			}
		}

		public void ActOnTargets(CombatCharacter[] targets)
		{
			if (!IsAlive)
				return;

			if (_combatAction == null)
			{
				Debug.LogWarning("No combat action set! Defaulting to basic attack.", this);
				_combatAction = _info.basicAttack;
			}

			bool missedAttack = false;
			float directDamage = 0f;

			switch (_combatAction.actionType)
			{
				case ActionType.Attack:
				case ActionType.Spell:
				case ActionType.Combo:
					directDamage = CalculateDamageWithEffects(_combatAction, out missedAttack);
					if (!_hasPaidResourceCostThisTurn)
					{
						ApplyResourceCosts(_combatAction);
						_hasPaidResourceCostThisTurn = true;
					}
					break;

				case ActionType.Item:
					UseItemAction useItemAction = _combatAction as UseItemAction;
					if (useItemAction?.Item is Consumable consumable)
					{
						consumable.Use(targets);
					}
					return;
				default:
					Debug.LogError("Unimplemented ActionType");
					return;
			}

			if (_combatAction.actionType != ActionType.Item)
			{
				foreach (CombatCharacter target in targets)
				{
					Debug.Log($"{directDamage} damage dealt using {_combatAction.actionName}");
					DisplayEnemyCombatInfo();

					var dmgCtx = new DamageContext
					{
						attacker = this,
						defender = target,
						action = _combatAction,
						baseDamage = directDamage,
						finalDamage = directDamage
					};

					ForEachEquippedEffect(eff => eff.OnBeforeDealDamage(dmgCtx, this));

					target.ChangeChangingStatValue(
						dmgCtx.finalDamage,
						_combatAction.showDamageText,
						StatType.Vitality,
						_info.characterClass,
						this,
						EffectType.None,
						_combatAction);

					ApplyBuffsAndEffects(target, missedAttack);

					bool actuallyHit = !missedAttack && !target.Dodge;

					var atkCtx = new AttackContext
					{
						attacker = this,
						defender = target,
						action = _combatAction,
						hit = actuallyHit,
						damageContext = dmgCtx
					};

					ForEachEquippedEffect(eff => eff.OnAfterAttack(atkCtx, this));
				}
			}

			ApplySelfEffectsIfNeeded();

			if (_combatAction.HasAgilityBuff)
				CombatManager.Instance.ApplyRecalculatedOrderToTurnOrder();

			foreach (CombatCharacter target in targets)
			{
				if (!target.IsAlive)
					CombatManager.Instance.CharacterDefeated(target);
			}

			Debug.Log($"[ActOnTargets] {_combatAction.actionName} hits {targets.Length} target(s). " +
					  $"show={_combatAction.showDamageText} base={directDamage}");
		}




		private float CalculateDamageWithEffects(CombatAction action, out bool missedAttack)
		{
			if (!action.hitSound.IsNull && action.PlayHitSoundThroughHitVFXEvent == false)
			{
				FMODUnity.RuntimeManager.PlayOneShot(action.hitSound);
			}

			missedAttack = false;
			float baseDamage = GetBaseDamage(action);
			float damageMultiplier = 1f;

			if (IsBlind && DidMissAttack())
			{
				Debug.Log("Attack missed due to blindness!");
				RemoveAttackEffectByHits();
				missedAttack = true;
				return 0f;
			}

			if (DmgIncrease)
			{
				damageMultiplier *= 2f;
			}
			else if (Weak)
			{
				damageMultiplier *= 0.5f;
			}

			RemoveAttackEffectByHits();
			return baseDamage * damageMultiplier;
		}

		private float GetBaseDamage(CombatAction action)
		{
			return action switch
			{
				BasicAttack basicAttack => IsEnemyOrBoss()
					? basicAttack.directDamage
					: basicAttack.directDamage,
				SpellAction spellAction => IsEnemyOrBoss()
					? spellAction.directDamage
					: spellAction.directDamage,
				ComboAction comboAction => comboAction.directDamage,
				_ => 0f
			};
		}

		private bool DidMissAttack()
		{
			float missChance = 0.5f;
			return Random.value > missChance;
		}

		private void ApplyResourceCosts(CombatAction action)
		{
			switch (action)
			{
				case SpellAction spellAction:
					if (_info.characterClass == CharacterClass.Enemy || _info.characterClass == CharacterClass.Boss)
						action.remainingCoolDown = action.CoolDown;

					Debug.Log($"{action.actionName} put on cooldown.");
					ChangeChangingStatValue(spellAction.manaCost, ShowDamageText.No, StatType.Mana);
					break;
			}
		}

		private void ApplyManaCosts(CombatAction action)
		{
			switch (action)
			{
				case SpellAction spellAction:
					ChangeChangingStatValue(spellAction.manaCost, ShowDamageText.No, StatType.Mana);
					break;

			}
		}

		private void ApplyBuffsAndEffects(CombatCharacter target, bool missedAttack)
		{
			foreach (Buff buff in _combatAction.buffs)
			{
				target.AddBuff(new Buff(buff.statToBuff, buff.buffType, buff.turnsActive, buff.value));
			}

			foreach (Effect effect in _combatAction.effects)
			{
				if (_combatAction.targetingType == TargetingType.Self)
				{
					AddEffect(new Effect(effect.EffectValue));
				}
				else if (effect.EffectValue != null && !missedAttack && !target.Dodge)
				{
					if (_combatAction.onlyAddEffectIfNegating)
					{
						target.NegateEffects(new Effect(effect.EffectValue), true);
					}
					else
					{
						target.AddEffect(new Effect(effect.EffectValue));
					}
				}
				else if (effect.EffectValue != null && target.Dodge)
				{
					target.NegateEffects(new Effect(effect.EffectValue), true);
				}
			}
		}

		private void ApplySelfEffectsIfNeeded()
		{
			if (_combatAction.actionType != ActionType.Spell ||
				_combatAction.targetingType
					is not (TargetingType.EnemiesAndSelf
					or TargetingType.SingleEnemyAndSelf
					or TargetingType.SinglePlayerAndSelf
					or TargetingType.AllPlayerAndSelf)) return;

			foreach (Effect selfEffect in _combatAction.selfEffects)
			{
				Debug.Log("Adding effect to self.");
				AddEffect(new Effect(selfEffect.EffectValue));
			}
		}

		private void DisplayEnemyCombatInfo()
		{
			if (_info.characterClass == CharacterClass.Enemy || _info.characterClass == CharacterClass.Boss)
			{
				var combatActionMenu = CombatManager.Instance.GetComponentInChildren<CombatActionMenu>();
				combatActionMenu.DisplayCombatInfoEnemy(_combatAction);
			}
		}

		public void RemoveAttackEffectByHits()
		{
			List<Effect> usedEffects = new();
			foreach (Effect effect in CurrentEffects.ToArray())
			{
				if (effect.EffectCategory != EffectCategories.Attack)
					continue;

				if (effect.hitsActive > 0 && effect.EffectCategory == EffectCategories.Attack)
				{
					effect.hitsActive--;
					if (effect.hitsPerStack > 0)
					{
						int expectedStacks = Mathf.CeilToInt((float)effect.hitsActive / effect.hitsPerStack);
						if (expectedStacks < effect.stacksCount)
						{
							effect.stacksCount = expectedStacks;
						}
					}
					Debug.Log("Reducing hits active" + this);
					if (effect.hitsActive == 0)
					{
						usedEffects.Add(effect);
					}
				}
			}

			foreach (Effect effect in usedEffects)
			{
				if (effect.EffectCategory == EffectCategories.Attack)
				{
					Debug.Log("Removing effect: " + effect);
					if (_info.characterClass == CharacterClass.Enemy || _info.characterClass == CharacterClass.Boss)
					{
						CombatManager.Instance.tokenUi.UpdateEffectsForEnemy(Index, CurrentEffects, false, effect);
					}
					else
					{
						CombatManager.Instance.tokenUi.UpdateEffectsForPlayer(PlayerIndex, CurrentEffects, false, effect);
					}
					StartCoroutine(RemoveEffectFromList(effect));
				}
			}

			usedEffects.Clear();
		}


		public void EndTurn()
		{
			Debug.Log($"[EndTurn] {name} action={_combatAction?.actionName} t={Time.time}\n{Environment.StackTrace}");
			_cachedActionTargets = null;

			ForEachEquippedEffect(eff => eff.OnTurnEnd(this));

			if (!IsAlive)
			{
				if (!IsEnemyOrBoss())
				{
					CombatManager.Instance.tokenUi.HideIconsPlayer(PlayerIndex);
				}
				else
				{
					CombatManager.Instance.tokenUi.HideIcons(Index);
				}
			}

			StopFlicker();
			StopActiveTurn();

			if (_info.characterClass < CharacterClass.Enemy)
			{
				_statUI.SetHighlight(false);
			}

			TurnsCompleted++;

			CombatManager.Instance.EndTurn();

			if (!IsEnemyOrBoss())
			{
				foreach (CombatAction combatAction in
						 _info.spells)
				{
					if (combatAction.remainingCoolDown != 0)
					{
						combatAction.remainingCoolDown =
							Math.Max(0,
								combatAction.remainingCoolDown - 1);
					}
				}
			}
			else
			{
				if ((_info.characterClass == CharacterClass.Enemy || _info.characterClass == CharacterClass.Boss) &&
					state != null)
					state.UpdateCooldowns();
			}
		}

		public void StopActiveTurn()
		{
			if (_activeTurn == null) return;

			CombatManager.Instance.StopCoroutine(_activeTurn);
			startingTurn = false;
			_activeTurn = null;
		}

		#endregion

		#region Equipment

		[SerializeField] private Equipment[] debugStartingEquipment;
		private bool _debugAutoEquipped = false;

		public void InitEquipment()
		{
			if (ItemSlots.Count <= 0)
			{
				foreach (ItemSlot slot in GetComponentsInChildren<ItemSlot>())
					ItemSlots.Add(slot);
			}

			if (debugStartingEquipment != null && debugStartingEquipment.Length > 0)
			{
				foreach (var eq in debugStartingEquipment)
				{
					if (eq == null) continue;
					Debug.Log($"[InitEquipment] Auto-equipping debug item '{eq.name}' on {name}");
					EquipItem(eq);
				}
			}

			ApplyEquipmentStats();
		}

		/// <summary>
		/// Real-game path: equip based on PlayerPartyManager runtime data instead of debugStartingEquipment.
		/// Called from CombatManager/CombatStarter AFTER Init(false).
		/// </summary>
		public void InitEquipmentFromPartyData(PlayerCharacterRuntimeData data)
		{
			// Make sure ItemSlots exists
			if (ItemSlots.Count <= 0)
			{
				foreach (ItemSlot slot in GetComponentsInChildren<ItemSlot>())
					ItemSlots.Add(slot);
			}

			// Clear any old items (e.g. from previous combat or debug)
			foreach (var slot in ItemSlots)
				slot._currentItem = null;

			if (data == null)
			{
				Debug.LogWarning("[CombatCharacter] InitEquipmentFromPartyData: no runtime data for " + name);
				ApplyEquipmentStats();
				return;
			}

			if (data.equipment != null)
			{
				if (data.equipment.weapon != null)
					EquipItem(data.equipment.weapon);
				if (data.equipment.armor != null)
					EquipItem(data.equipment.armor);
				if (data.equipment.accessory1 != null)
					EquipItem(data.equipment.accessory1);
				if (data.equipment.accessory2 != null)
					EquipItem(data.equipment.accessory2);
			}

			ApplyEquipmentStats();
		}

		private void EnsureDebugEquipmentEquipped()
		{
			if (_debugAutoEquipped) return;

			if (debugStartingEquipment == null || debugStartingEquipment.Length == 0)
			{
				Debug.Log($"[EnsureDebugEquipmentEquipped] No debugStartingEquipment set on {name}");
				_debugAutoEquipped = true;
				return;
			}

			if (ItemSlots == null || ItemSlots.Count == 0)
			{
				foreach (ItemSlot slot in GetComponentsInChildren<ItemSlot>())
					ItemSlots.Add(slot);
			}

			bool hasAnyItem = false;
			foreach (var slot in ItemSlots)
			{
				if (slot != null && slot._currentItem != null)
				{
					hasAnyItem = true;
					break;
				}
			}

			if (hasAnyItem)
			{
				Debug.Log($"[EnsureDebugEquipmentEquipped] {name} already has equipped items, skipping debug auto-equip.");
				_debugAutoEquipped = true;
				return;
			}

			Debug.Log($"[EnsureDebugEquipmentEquipped] Auto-equipping debug items on {name}");
			foreach (var eq in debugStartingEquipment)
			{
				if (eq == null) continue;
				Debug.Log($"[EnsureDebugEquipmentEquipped] Equipping '{eq.name}' on {name}");
				EquipItem(eq);
			}

			bool equippedAny = false;
			foreach (var slot in ItemSlots)
			{
				string itemName = slot._currentItem != null ? slot._currentItem.name : "null";
				Debug.Log($"[EnsureDebugEquipmentEquipped] After EquipItem: slot {slot.slotID} ({slot.EquipmentType}) has {itemName}", this);

				if (slot._currentItem != null)
					equippedAny = true;
			}

			if (equippedAny)
			{
				ApplyEquipmentStats();
				_debugAutoEquipped = true;
				return;
			}

			var debugEq = debugStartingEquipment[0];
			if (debugEq == null)
			{
				Debug.LogWarning("[EnsureDebugEquipmentEquipped] First debugStartingEquipment entry is null on " + name, this);
				_debugAutoEquipped = true;
				return;
			}

			bool forced = false;
			foreach (var slot in ItemSlots)
			{
				if (slot == null) continue;

				if (slot.EquipmentType == EquipmentTypes.Accessory)
				{
					Debug.LogWarning(
						"[EnsureDebugEquipmentEquipped] Forcing '" + debugEq.name +
						"' into slot " + slot.slotID + " on " + name + " (bypassing ItemSlot checks)",
						slot);

					slot._currentItem = debugEq;

					forced = true;
					break;
				}
			}

			if (forced)
			{
				ApplyEquipmentStats();
				Debug.Log("[EnsureDebugEquipmentEquipped] Forced debug trinket equipped on " + name, this);
			}
			else
			{
				Debug.LogError("[EnsureDebugEquipmentEquipped] Could not find an Accessory slot to force equip debug trinket on " + name, this);
			}

			_debugAutoEquipped = true;
		}

		public void EquipItem(Equipment item, ItemSlot itemSlot = null)
		{
			if (item == null)
			{
				Debug.LogError("[EquipItem] item is null on " + name, this);
				return;
			}

			if (ItemSlots.Count <= 0)
			{
				foreach (ItemSlot slot in GetComponentsInChildren<ItemSlot>())
					ItemSlots.Add(slot);

				Debug.Log("[EquipItem] Rebuilt ItemSlots list. Count=" + ItemSlots.Count + " on " + name, this);
			}

			Debug.Log(
				"[EquipItem] Trying to equip '" + item.name +
				"' (ItemType=" + item.ItemType +
				", EquipType=" + item.EquipmentType +
				") on " + name,
				this);

			if (itemSlot == null || !ItemSlots.Contains(itemSlot))
			{
				ItemSlot chosen = null;

				foreach (ItemSlot slot in ItemSlots)
				{
					string currentName = slot._currentItem != null ? slot._currentItem.name : "null";
					Debug.Log(
						"[EquipItem] Candidate slot " + slot.slotID +
						" type=" + slot.EquipmentType +
						" item=" + currentName,
						slot);

					if (slot.EquipmentType == item.EquipmentType && slot._currentItem == null)
					{
						chosen = slot;
						break;
					}
				}

				if (chosen != null)
				{
					Debug.Log("[EquipItem] Found slot " + chosen.slotID + " for '" + item.name + "'", chosen);
					chosen.SetCurrentItem(item);
				}
				else
				{
					Debug.LogWarning(
						"[EquipItem] No compatible empty slot found for '" + item.name +
						"' on " + name + ". Check ItemType and EquipmentType.",
						this);
				}
			}
			else
			{
				Debug.Log("[EquipItem] Using explicit slot " + itemSlot.slotID + " for '" + item.name + "'", itemSlot);
				itemSlot.SetCurrentItem(item);
			}

			ApplyEquipmentStats();
		}

		public void UnequipItem(Equipment item, ItemSlot itemSlot = null)
		{
			Equipment previous = null;

			if (itemSlot == null)
			{
				foreach (ItemSlot slot in ItemSlots)
				{
					if (slot._currentItem == item)
					{
						previous = slot._currentItem as Equipment;
						slot.RemoveCurrentItem();
						break;
					}
				}
			}
			else
			{
				if (itemSlot._currentItem != null && itemSlot._currentItem == item)
				{
					previous = itemSlot._currentItem as Equipment;
					itemSlot.RemoveCurrentItem();
				}
			}

			if (previous != null && previous.Effects != null)
			{
				foreach (var eff in previous.Effects)
				{
					if (eff != null)
						eff.OnUnequip(this);
				}
			}

			ApplyEquipmentStats();
		}


		void ApplyEquipmentStats()
		{
			if (_stats == null) return;

			// clear all equipment modifiers first
			foreach (BaseStat stat in _stats)
				stat.ResetEquipmentModifier();

			// apply modifiers from currently equipped items
			foreach (ItemSlot slot in ItemSlots)
			{
				if (slot == null) continue;

				if (slot._currentItem is not Equipment eq)
					continue;

				if (eq.Stats == null)
					continue;

				foreach (ItemStat stat in eq.Stats)
				{
					// ItemStat is a struct, so it can't be null
					_stats[(int)stat.StatToEffect].AddEquipmentModifier(stat);
				}
			}

			// refresh UI if this character has a stat UI bound
			_statUI?.UpdateStatUI(_stats);
		}


		public void PreviewEquipmentStats(Equipment equipment, ItemSlot itemSlot = null)
		{
			foreach (BaseStat stat in _stats)
			{
				stat.ResetPreviewEquipmentModifier();
			}

			Equipment replacedEquipment = null;
			ItemSlot tempReplaceSlot = null;
			bool availableSlot = false;

			if (itemSlot != null)
			{
				if (itemSlot._currentItem != null)
					replacedEquipment = itemSlot._currentItem;
			}
			else
			{
				foreach (ItemSlot slot in ItemSlots)
				{
					if (slot.EquipmentType == equipment.EquipmentType)
					{
						if (tempReplaceSlot == null)
							tempReplaceSlot = slot;

						if (slot._currentItem == null)
						{
							availableSlot = true;
						}
					}
				}

				if (!availableSlot)
				{
					replacedEquipment = tempReplaceSlot._currentItem;
				}
			}

			if (equipment != null && equipment.Stats != null)
			{
				foreach (ItemStat stat in equipment.Stats)
					_stats[(int)stat.StatToEffect].AddPreviewEquipments(stat);
			}

			if (replacedEquipment != null && replacedEquipment.Stats != null)
			{
				foreach (ItemStat stat in replacedEquipment.Stats)
					_stats[(int)stat.StatToEffect].AddPreviewEquipments(stat, replaceEquipment: true);
			}
		}

		public IEnumerable<Equipment> EquippedItems
		{
			get
			{
				if (ItemSlots == null)
				{
					yield break;
				}

				foreach (var slot in ItemSlots)
				{
					if (slot == null) continue;
					if (slot._currentItem is Equipment eq)
						yield return eq;
				}
			}
		}

		public void ForEachEquippedEffect(System.Action<EquipmentEffectBase> action)
		{
			Debug.Log($"[ForEachEquippedEffect] Called for {name}");

			int slotCount = ItemSlots?.Count ?? 0;
			Debug.Log($"[ForEachEquippedEffect] ItemSlots count = {slotCount}");

			if (ItemSlots != null)
			{
				foreach (var s in ItemSlots)
				{
					if (s == null)
					{
						Debug.Log("[ForEachEquippedEffect] - Slot is NULL");
						continue;
					}

					string itemName = s._currentItem != null ? s._currentItem.name : "null";
					Debug.Log($"[ForEachEquippedEffect] - SlotID={s.slotID}, type={s.EquipmentType}, item={itemName}");
				}
			}

			if (action == null)
			{
				Debug.Log("[ForEachEquippedEffect] Action is null, returning.");
				return;
			}

			int equippedCount = 0;
			foreach (var eq in EquippedItems)
			{
				equippedCount++;
				if (eq == null)
				{
					Debug.Log("[ForEachEquippedEffect] Found null Equipment instance.");
					continue;
				}

				int effCount = eq.Effects != null ? eq.Effects.Count : 0;
				Debug.Log($"[ForEachEquippedEffect] Equipment='{eq.name}', Effects.Count={effCount}");

				if (eq.Effects == null) continue;

				foreach (var eff in eq.Effects)
				{
					if (eff == null)
					{
						Debug.Log($"[ForEachEquippedEffect]   null effect on {eq.name}");
						continue;
					}

					Debug.Log($"[ForEachEquippedEffect]   Running effect {eff.name} on {name}");
					action(eff);
				}
			}

			if (equippedCount == 0)
			{
				Debug.Log("[ForEachEquippedEffect] No equipped Equipment found on this character.");
			}
		}

		#endregion

		#region Animation

		public void ForceEndCurrentAction()
		{
			_pendingHitEvents = 0;
			_finishEventSeen = true;
		}


		void SetDamageAnimation(CharacterClass cc, CombatCharacter attackerCharacter = null,
			CombatAction combatAction = null, EffectType effectType = EffectType.None)
		{

			if (_enemyDamageAnimationCoroutine != null)
				StopCoroutine(_enemyDamageAnimationCoroutine);
			if (attackerCharacter != null)
				_enemyDamageAnimationCoroutine = StartCoroutine(SetEnemyCharacterDamageAnimation(cc, attackerCharacter));
			else
				_enemyDamageAnimationCoroutine = StartCoroutine(SetEnemyCharacterDamageAnimation(cc));


			if (hurtSound != null)
			{
				AudioManager.Instance.PlayHurtSound(hurtSound);
			}

			if (attackerCharacter != null)
			{
				if (attackerCharacter.CombatAction != null)
				{
					if (attackerCharacter.CombatAction.hitSound.IsNull)
					{
						Debug.Log("hitSound is null or invalid.");
					}
					else
					{
#if UNITY_EDITOR
						Debug.Log($"Playing sound: {attackerCharacter.CombatAction.hitSound.Path}");
#endif
						RuntimeManager.PlayOneShot(attackerCharacter.CombatAction.hitSound);
					}
				}
				else
				{
					Debug.Log("ActiveCharacter or CombatAction is null.");
				}

				if (attackerCharacter.CombatAction.HitSound != null)
				{
					Debug.Log("Trying to play hit audio");
					AudioManager.Instance.PlayHitSound(attackerCharacter.CombatAction.HitSound
						.GetComponent<StudioEventEmitter>());
				}

				if (combatAction != null)
				{
					if (combatAction.ActionVFX != null)
					{
						Instantiate(combatAction.ActionVFX, attackerCharacter.transform.GetChild(3).position,
							Quaternion.identity);
					}
				}
			}
			else
			{
				if (effectType == EffectType.Burning || effectType == EffectType.Bleeding ||
					effectType == EffectType.Poison)
				{
				}
			}

			if (!IsEnemyOrBoss())
			{
				if (!spriteAnimator) return;
				spriteAnimator.SetBool(AnimatorExtension.ANIMATOR_IS_LOWHP, IsLowHP);
				spriteAnimator.SetBoolForSeconds(AnimatorExtension.ANIMATOR_IS_HURTING, true,
					spriteAnimator.hurtDuration);
				spriteAnimator.SetBoolAfterSeconds(AnimatorExtension.ANIMATOR_IS_DEAD, !IsAlive,
					spriteAnimator.hurtDuration);

				if (attackerCharacter != null)
				{
					if (attackerCharacter._combatAction.HitVFX == null)
					{
						if (attackerCharacter.AttackEffectEnemy == null)
						{
							Debug.Log("Attacker didint have attack effect set, Using biteEffect");
							Instantiate(VFXPrefabHolder.biteEffect, transform.GetChild(2).position,
								Quaternion.identity);
						}
						else
						{
							Instantiate(attackerCharacter.AttackEffectEnemy, transform.GetChild(2).position,
								Quaternion.identity);
						}
					}
					else
					{
						Instantiate(attackerCharacter._combatAction.HitVFX, transform.GetChild(2).position,
							Quaternion.identity);
						Debug.Log("Playing hit vfx");
					}

					if (attackerCharacter._combatAction.ActionVFX != null)
					{
						Instantiate(attackerCharacter._combatAction.ActionVFX,
							attackerCharacter.transform.GetChild(2).position, Quaternion.identity);
					}
				}
				else
				{
					if (effectType is EffectType.Burning or EffectType.Bleeding or EffectType.Poison)
					{
					}
				}
			}
			else
			{
				if (combatAction != null)
				{
					if (combatAction.HitVFX == null)
					{
						if (cc == CharacterClass.Lydia)
						{
							if (IsAlive)
							{
								Instantiate(VFXPrefabHolder.slashEffect, transform.GetChild(3).position,
									Quaternion.identity);
								GameObject drain = Instantiate(VFXPrefabHolder.DrainEffect,
									transform.GetChild(3).position,
									Quaternion.Euler(0, 105,
										0));
							}
						}
					}
					else
					{
					}
				}
				else
				{
					if (effectType == EffectType.Burning || effectType == EffectType.Bleeding ||
						effectType == EffectType.Poison)
					{
					}
				}

				Debug.Log("TODO: Generalize getting attack effects");
				StartCoroutine(DeathHitEffects(cc));
			}
		}

		#region Play VFXs
		public void PlayUseVFX(CombatAction combatAction)
		{
			if (combatAction.actionType == ActionType.Item)
			{
				UseItemAction useItemAction = combatAction as UseItemAction;
				if (useItemAction?.Item is Consumable consumable)
				{
					if (consumable.itemUseEffect != null)
						Instantiate(consumable.itemUseEffect, transform);
					else
						Debug.LogWarning(consumable.itemName + " itemUseEffect is null");
				}
				ActOnTargets(CombatManager.Instance._targets.ToArray());
			}
		}

		public void PlayHitVFX(CombatAction combatAction, CombatCharacter[] targets)
		{
			OnHitAppliedFromAnim();
			var snap = targets?.Where(t => t && t != this && t.IsAlive).ToArray();
			if (snap == null || snap.Length == 0) return;

			EffectManager.Instance.ActivateEffect(PostProcessingEffects.ChromaticAberration);
			CombatManager.Instance.CameraController.ActivateCameraEffect(combatAction.hitCameraTrigger.ToString());

			foreach (var t in snap)
			{
				if (combatAction.HitVFX != null)
					Instantiate(combatAction.HitVFX, t.transform.GetChild(3).position, Quaternion.identity);
			}
			Debug.Log($"[Damage/HitVFX] {combatAction.actionName} " +
		  $"snap=[{string.Join(",", targets.Select(x => x ? x.name : "null"))}] " +
		  $"global=[{string.Join(",", (CombatManager.Instance._targets ?? new List<CombatCharacter>()).Select(x => x ? x.name : "null"))}] " +
		  $"self={name}");
			if (!combatAction.hitSound.IsNull && combatAction.PlayHitSoundThroughHitVFXEvent == true)
			{
				FMODUnity.RuntimeManager.PlayOneShot(combatAction.hitSound);
			}
			ActOnTargets(snap);
		}

		public void PlayActionVFX(CombatAction combatAction)
		{
			if (combatAction.ActionVFX != null)
				Instantiate(combatAction.ActionVFX, transform.GetChild(2).position, Quaternion.identity);
			else
				Debug.LogWarning(combatAction.actionName + " didint have ActionVFX");
		}
		#endregion
		#region Play SFXs
		public void PlayActionSound(CombatAction combatAction)
		{
			if (CombatAction.actionSound.IsNull)
			{
				Debug.Log("actionSound is null or invalid.");
			}
			else
			{
#if UNITY_EDITOR
				Debug.Log($"Playing sound: {CombatAction.actionSound.Path}");
#endif
				RuntimeManager.PlayOneShot(CombatAction.actionSound);
			}
		}
		public void PlayHitSound(CombatAction combatAction)
		{
			if (CombatAction.hitSound.IsNull)
			{
				Debug.Log("hitSound is null or invalid.");
			}
			else
			{
#if UNITY_EDITOR
				Debug.Log($"Playing sound: {CombatAction.hitSound.Path}");
#endif
				RuntimeManager.PlayOneShot(CombatAction.hitSound);
			}
		}
		#endregion
		bool VFXExists(string vfxName)
		{
			return GameObject.Find(vfxName) != null;
		}

		IEnumerator DeathHitEffects(CharacterClass cc)
		{
			yield return new WaitForSeconds(0.29f);

			if (cc == CharacterClass.Jonah)
			{
				if (!IsAlive)
				{
					Instantiate(VFXPrefabHolder.ArrowEffect, transform.GetChild(3).position, Quaternion.identity);
					Instantiate(VFXPrefabHolder.slashEffect, transform.GetChild(3).position, Quaternion.identity);
				}
			}

			if (cc == CharacterClass.Lydia)
			{
				if (!IsAlive)
				{
					Instantiate(VFXPrefabHolder.slashEffect, transform.GetChild(3).position, Quaternion.identity);
					GameObject drain = Instantiate(VFXPrefabHolder.DrainEffect, transform.GetChild(3).position,
						Quaternion.Euler(0, 105,
							0));
				}
			}
			else
			{
				if (!IsAlive)
				{
					if (!VFXExists("Slash01c(Clone)"))
					{
						Instantiate(VFXPrefabHolder.slashEffect, transform.GetChild(3).position, Quaternion.identity);
					}
				}
			}
		}


		IEnumerator SetEnemyCharacterDamageAnimation(CharacterClass cc, CombatCharacter attacker = null)
		{
			SpriteRenderer sr = transform.GetChild(TARGET_SPRITE_INDEX)
								.GetComponent<SpriteRenderer>();

			if (_baseMat == null)
				_baseMat = sr.sharedMaterial;

			if (_blinkMat == null)
			{
				_blinkMat = new Material(_baseMat);
				_blinkMat.name += "-BlinkCopy";
				_blinkMat.hideFlags = HideFlags.DontSave;
			}

			if (!ReferenceEquals(_blinkMat, _baseMat))
				_blinkMat.CopyPropertiesFromMaterial(_baseMat);

			_blinkMat.color = _damageColor;
			sr.material = _blinkMat;

			if (!IsAlive)
			{
				if (attacker != null)
					attacker.ForceEndCurrentAction();

				if (dissolveMaterial != null)
					sr.material = dissolveMaterial;


				Effect tauntEffect = null;
				foreach (Effect effect in CurrentEffects)
				{
					if (effect.EffectType == EffectType.Taunt)
					{
						Debug.Log("Removed taunt");
						tauntEffect = effect;
						CurrentEffects.Remove(tauntEffect);
						break;
					}
					if (effect.EffectType == EffectType.Stun)
					{
						if (StunEffect != null)
						{
							Destroy(StunEffect);
							StunEffect = null;
						}
					}
				}

				Time.timeScale = 1f;
				if (!doingCameraDying)
				{
					doingCameraDying = true;
					yield return new WaitForSeconds(0.3f);
					if (_info.characterClass == CharacterClass.Enemy || _info.characterClass == CharacterClass.Boss)
					{
						Debug.Log("Starting Dissolve");
						StartCoroutine(Dissolve());
					}

					GameObject defeatSmoke = Instantiate(VFXPrefabHolder.deathSmoke, transform.GetChild(3).position,
						Quaternion.identity);
					yield return new WaitForSeconds(0.7f);


					yield return new WaitForSeconds(0.2f);
					Time.timeScale = 1f;
					CombatManager.Instance.CameraController.cameraBrain.DefaultBlend.Time =
						0.5f;
					if (_info.characterClass == CharacterClass.Enemy ||
						_info.characterClass ==
						CharacterClass.Boss)
						doingCameraDying = false;
				}
			}
			yield return new WaitForSeconds(_damageAnimationTime);

			sr.material = _baseMat;
			sr.color = Color.white;




			if (_enemyDamageAnimationCoroutine != null)
				StopCoroutine(_enemyDamageAnimationCoroutine);
			_enemyDamageAnimationCoroutine = null;
		}

		IEnumerator Dissolve()
		{
			yield return new WaitForSeconds(0.1f);

			float t = 0;
			while (t < 1f)
			{
				t += Time.deltaTime;
				Vector3 newPosition = gameObject.transform.position;
				newPosition.x -= Time.deltaTime * 1.5f;
				gameObject.transform.position = newPosition;
				if (dissolveMaterial != null)
				{
					SpriteRenderer characterSprite = gameObject.transform.GetChild(TARGET_SPRITE_INDEX)
						.GetComponent<SpriteRenderer>();
					characterSprite.material = dissolveMaterial;
					gameObject.transform.GetChild(TARGET_SPRITE_INDEX).GetComponent<SpriteRenderer>().material
						.SetFloat("_Dissolve", t);
				}
				else
				{
					SpriteRenderer spriteRenderer = transform.GetComponentInChildren<SpriteRenderer>();
					Color color = spriteRenderer.material.color;

					color.a = Mathf.Lerp(1f, 0f, t);


					spriteRenderer.material.color = color;
				}

				yield return null;
			}


			CombatManager.Instance.CharacterDefeated(this);
			if (CurrentEffects != null)
			{
				foreach (Effect effect in
						 CurrentEffects
							 .ToArray())
				{
					CurrentEffects.Remove(effect);
				}
			}

			CombatManager.Instance.tokenUi.HideIcons(Index);
		}

		#endregion
	}

	[Serializable]
	public class StatEntry
	{
		public int statType;
		public float value;
	}

	[Serializable]
	public class BuffListEntry
	{
		public int statType;
		public List<Buff> buffs = new();
	}
	[Serializable]
	public class CharacterData
	{
		public string CharacterName;
		public int Level;

		// Only used as a fallback if UpgradeData is missing
		public int targetLevel;

		[SerializeField] private List<StatEntry> statValuesSerialized = new();
		[NonSerialized] public Dictionary<int, float> StatValues = new();

		[SerializeField] private List<BuffListEntry> activeBuffsSerialized = new();
		[NonSerialized] public Dictionary<int, List<Buff>> ActiveBuffs = new();

		public List<ItemSlotData> ItemSlotsData = new();

		public BasicAttackData attackData;
		public List<SpellActionData> spellDatas = new();
		public List<ComboActionData> comboDatas = new();

		public string CharacterID;

		// Optional debug/info
		public float CurrentHealth;
		public float MaxHealth;

		public StatUpgradeData UpgradeData;

		public void SerializeStatValues()
		{
			statValuesSerialized.Clear();
			foreach (var kvp in StatValues)
				statValuesSerialized.Add(new StatEntry { statType = kvp.Key, value = kvp.Value });
		}

		public void DeserializeStatValues()
		{
			StatValues = new Dictionary<int, float>();
			foreach (var entry in statValuesSerialized)
				StatValues[entry.statType] = entry.value;
		}

		public void SerializeActiveBuffs()
		{
			activeBuffsSerialized.Clear();
			foreach (var kvp in ActiveBuffs)
			{
				activeBuffsSerialized.Add(new BuffListEntry
				{
					statType = kvp.Key,
					buffs = kvp.Value
				});
			}
		}

		public void DeserializeActiveBuffs()
		{
			ActiveBuffs = new Dictionary<int, List<Buff>>();
			foreach (var entry in activeBuffsSerialized)
				ActiveBuffs[entry.statType] = entry.buffs ?? new List<Buff>();
		}

		public CharacterData(CombatCharacter cc)
		{
			if (cc == null) return;

			CharacterName = cc.Info.characterName;
			Level = cc.level;

			// Save this as a fallback only (if you later load a save that doesn't have UpgradeData)
			targetLevel = cc.Info.targetLevel;

			CharacterID = cc.characterID;

			if (cc.Stats[(int)StatType.Vitality] is ChangingStat hp)
			{
				CurrentHealth = hp.Value;
				MaxHealth = hp.MaxValue;
			}

			// Save upgrade data (enables restoring your old attribute-leveling system)
			UpgradeData = new StatUpgradeData();
			Array.Copy(cc.Info.upgradeData.pointsInvested, UpgradeData.pointsInvested, UpgradeData.pointsInvested.Length);
			UpgradeData.unspentPoints = cc.Info.upgradeData.unspentPoints;

			// Save stats:
			// - ChangingStats (HP/MP): save CURRENT value only
			// - StaticStats: save BASE final stat (derived from upgrades, not from equipment/buffs)
			StatValues.Clear();
			ActiveBuffs.Clear();

			for (int i = 0; i < cc.Stats.Length; i++)
			{
				StatType st = (StatType)i;

				if (st == StatType.Vitality || st == StatType.Mana)
				{
					StatValues[i] = cc.Stats[i].Value;
				}
				else
				{
					StatValues[i] = cc.Info.GetFinalStat(st);
				}

				ActiveBuffs[i] = cc.Stats[i].GetBuffList() ?? new List<Buff>();
			}

			SerializeActiveBuffs();
			SerializeStatValues();

			// Save equipped items
			ItemSlotsData.Clear();
			foreach (ItemSlot slot in cc.ItemSlots)
			{
				if (slot == null || slot._currentItem == null) continue;

				ItemSlotData data = new(slot.slotID, slot._currentItem);
				if (!ItemSlotsData.Contains(data))
					ItemSlotsData.Add(data);
			}

			// Save actions
			if (cc.Info.basicAttack != null)
				attackData = new BasicAttackData(cc.Info.basicAttack);

			spellDatas.Clear();
			foreach (SpellAction action in cc.Info.spells)
				if (action != null)
					spellDatas.Add(new SpellActionData(action));

			comboDatas.Clear();
			foreach (ComboAction action in cc.Info.combos)
				if (action != null)
					comboDatas.Add(new ComboActionData(action));
		}

		public void LoadData(CombatCharacter cc)
		{
			if (cc == null || cc.Info == null || cc.Stats == null) return;

			DeserializeStatValues();
			DeserializeActiveBuffs();

			// 1) Restore upgrade data FIRST (source of truth for targetLevel + final stats)
			if (UpgradeData != null)
			{
				Array.Copy(UpgradeData.pointsInvested, cc.Info.upgradeData.pointsInvested, UpgradeData.pointsInvested.Length);
				cc.Info.upgradeData.unspentPoints = UpgradeData.unspentPoints;

				// This should set targetLevel based on pointsInvested
				cc.Info.UpdateTargetLevelFromUpgrades();
			}
			else
			{
				// Backward compatibility: old saves with no UpgradeData
				cc.Info.targetLevel = targetLevel;
			}

			// 2) Rebuild base stats from Info (no equipment/buff inflation)
			//    Also restore buffs now (buffs can affect combat, turn order, etc.)
			for (int i = 0; i < cc.Stats.Length; i++)
			{
				BaseStat stat = cc.Stats[i];
				if (stat == null) continue;

				StatType st = (StatType)i;

				int finalStat = cc.Info.GetFinalStat(st);
				stat.SetBaseStat(finalStat);

				if (ActiveBuffs != null && ActiveBuffs.TryGetValue(i, out var buffs) && buffs != null)
				{
					foreach (Buff buff in buffs)
						stat.AddBuff(buff);
				}
			}

			// 3) Load equipment, then apply equipment stats (ensures max HP/MP etc are correct)
			if (ItemSlotsData != null)
			{
				foreach (ItemSlotData data in ItemSlotsData)
					data.LoadData(cc);
			}

			cc.RefreshEquipmentStats();

			// 4) Restore current HP/MP LAST (after max is finalized by base + buffs + equipment)
			for (int i = 0; i < cc.Stats.Length; i++)
			{
				StatType st = (StatType)i;
				if (st != StatType.Vitality && st != StatType.Mana) continue;

				if (StatValues != null && StatValues.TryGetValue(i, out float savedValue))
					cc.Stats[i].SetStat(savedValue);
			}

			// 5) Clamp to max in case max shrank (gear removed, upgrade system changed, etc.)
			if (cc.Stats[(int)StatType.Vitality] is ChangingStat hp)
				hp.SetStat(Mathf.Min(hp.Value, hp.MaxValue));

			if (cc.Stats[(int)StatType.Mana] is ChangingStat mp)
				mp.SetStat(Mathf.Min(mp.Value, mp.MaxValue));
		}

#if UNITY_EDITOR
		[CustomEditor(typeof(CombatCharacter))]
		public class CharacterEditor : Editor
		{
			public override void OnInspectorGUI()
			{
				CombatCharacter character = (CombatCharacter)target;

				DrawDefaultInspector();

				if (character.characterClassForInspector == CombatCharacter.CharacterClassForInspector.Player)
				{
					EditorGUILayout.PropertyField(serializedObject.FindProperty("attackSound"));
					EditorGUILayout.PropertyField(serializedObject.FindProperty("hurtSound"));
				}
				else
				{
					EditorGUILayout.PropertyField(serializedObject.FindProperty("AttackEffectEnemy"));
				}

				serializedObject.ApplyModifiedProperties();
			}
		}
#endif
	}
}
