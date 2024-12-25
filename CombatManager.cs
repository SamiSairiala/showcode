using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Skydome.UI;
using Skydome.Animations;
using System.Linq;
using Skydome.Combat.Effects;
using Skydome.Tools;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

namespace Skydome.Combat
{
	// TODO: Hide/Remove unnessacery info from inspector
	public class CombatManager : MonoBehaviour
	{
		/* Combat flow:
         *
         * Started from overworld
         * - overworld has needed data:
         *   - enemy party (and player party)
         *   - scene
         *
         * Turn order
         * - based on character (combat) speed
         *   - larger speed means more turns
         *     - 10 speed gets 2 turns when 5 speed gets 1
         * - visualize next N turns
         * - start next character's turn
         * - combat over check
         *
         * Character turn
         * - show active character
         * - play animations and sounds
         * - apply damage and effects to targets
         * - end turn
         * - player turn
         *   - show ui
         *     - show highlighted action info
         *     - show targets for highlighted action
         *     - highlight targeted characters in visual turn order
         * - enemy turn
         *   - random action
         *   - random target
         *     - show target
         *
         * End of combat
         * - xp, items, currency etc.
         * - return to overworld
         *
         */

		/* Camera flow:
         * Zoomout when coming to combat
         * Combat Camera
         * Combat char camera when targeting
         * Combat action camera when attacking/doing action
         * Zoom camera on death
         */
		public static CombatManager Instance;

		public static CombatCharacter ActiveCharacter
		{
			get => Instance._activeCharacter;
			set
			{
				if (Instance._activeCharacter != null)
					Instance._activeCharacter.IsActiveCharacter = false;
				Instance._activeCharacter = value;
				if (Instance._activeCharacter != null)
					Instance._activeCharacter.IsActiveCharacter = true;
			}
		}
		private CombatCharacter _activeCharacter;
		public CombatCharacter ActiveCharacterPublic => ActiveCharacter; // temp
		public static CombatActionMenu CombatActionMenu;
		public static bool doneSTOA, doneETOA, doneSNTOI;

		[FormerlySerializedAs("_cameraController")] public CombatCameraController CameraController;

		public TokenUI tokenUi;
		public CombatNamesUI combatnamesUI;
		public DamageNumbersUI damageNumbersUI;
		public CombatCharacter ActiveCharVisible;
		[Header("Colors for Damage Numbers")] public Color ReducedDamageColor;
		public Color NormalColor;
		public Color IncreasedDamageColor;
		[Space] public static CombatCharacter[] EnemyInstances, PlayerInstances;
		[HideInInspector] public CombatCharacter[] EnemyInstancesPublic, PlayerInstancesPublic;

		public bool IsTargeting => _targets.Count > 0;

		public bool IsBossFight { get; private set; } = false;
		public bool CombatEnded { get; private set; } = false;
		public bool CombatWon { get; private set; } = false;

		public CombatAction CurrentCombatAction
		{
			get { return currentCombatAction; }
		}

		#region variables

		[HideInInspector] public float DamageCurve = 1.5f, DamageNormalize = 1.5f, DamageFactor = 1.225f;

		//[SerializeField] Transform waypoint0, waypoint1, waypoint2, waypoint3; //use these if decide to use camera to moving towards specific enemy

		public Tools.SDScene sceneToLoadAfterCombat;

		[Header("Combat")] [SerializeField] GameObject _combatButtonPrefab;
		[SerializeField] CombatEndUI CombatEndUI;
		[SerializeField] float _delayBeforeEndUI = 1f;
		[SerializeField] public CombatCharacter[] _debugPlayers, _debugEnemies;
#if UNITY_EDITOR
		public bool UseDebugPlayersList = false;
#endif

		[Header("Combat positions")]
		[SerializeField]
		Transform _combatSpot;

		[SerializeField] Transform _combatEnterSpot;

		[Header("Turn order")]
		[SerializeField]
		Image _activeCharacterGlow;

		[SerializeField] Image _activeCharacterIcon;
		[SerializeField] List<AnimationEvent> _currentTurnOrderEvents, _nextTurnOrderEvents;

		[Header("Animation values")]
		[Header("Lower this if want players to move to combat faster when entering combat scene")]
		[SerializeField]
		float _minGroupMoveDuration = 0.75f;

		[Space] [SerializeField] float _maxGroupMoveDuration = 1.25f, _moveToActionDuration = 0.25f;

		[SerializeField] GameObject _endUI;

		#region local

		// turn order
		List<CombatCharacter> _orderedByTurnSpeed = new (),
			_turnOrder = new (),
			_turnOrderPreview = new (),
			_currentTurnOrder = new (),
			_nextTurnOrder = new ();

		public int _turnIndex,
			_visibleIconCount,
			_nextTurnVisibleIconCount,
			_deadIconCount; // temp public for debugging.

		bool _turnOrderAnimationIsOver, _waitingToAct;

		// targeting
		public readonly ObservableList<CombatCharacter> _targets = new ();
		public int _enemyTargetIndex, _playerTargetIndex;
		TargetingType _targetingType;

		// other
		CombatAction currentCombatAction;
		Coroutine _startCombat;
		Coroutine _startTurn;
		Coroutine _fleeCombat;
		public GameObject lights;

		enum MovePlayersType
		{
			CombatEnter,
			CombatExit
		}

		Coroutine _moveActivePlayer;
		List<Coroutine> _movePlayers = new ();
		float timeoutDuration = 10f; // Timeout duration in seconds, adjust as needed
		float elapsedTime = 0f;

		[Header("Debug")] public bool infiniteHealth = false;

		// temp
		Transform _tempButton;
		CombatCharacter _tempCC;
		List<CombatCharacter> _tempTurnOrder = new (), _tempResultList = new ();

		#endregion

		#endregion

		private void Awake()
		{
			if (Instance != null && Instance != this)
				Debug.LogWarning("Overriding CombatManager Instance! Old instance as context", Instance);
			Instance = this;

			CameraController = FindFirstObjectByType<CombatCameraController>(FindObjectsInactive.Include);

			_targets.ListChanged += (_, e) => { SetTargets(_targets, e); };

			CombatActionMenu = GetComponentInChildren<CombatActionMenu>(includeInactive: true);
			// hide action menu
			CombatActionMenu.ChangeActiveMenu(CombatActionMenuType.Hide);

			EnableCombatUI(false);
		}

		private void Update() // Temp to debug stuff really not needed delete when done with this.
		{
			ActiveCharVisible = ActiveCharacter;
			PlayerInstancesPublic = PlayerInstances;
		}

		public IEnumerator StuckEndTurn()
		{
			if (ActiveCharacter._info.characterClass > CharacterClass.Enemy)
			{
				while (elapsedTime < timeoutDuration)
				{
					elapsedTime += Time.deltaTime;
					yield return null;
				}

				if (elapsedTime >= timeoutDuration)
				{
					Debug.LogError("MoveCharacterToPosition timed out.");
					ActiveCharacter.EndTurn();
				}
			}
		}

		#region combat begin

		// In player partymanager add "new" chars to playerinstances to have new chars they will need to be added in CombatStarter // This method is called from scenetransit
		public void LoadCombatCharacterInstances(CombatCharacter[] enemies = null, CombatCharacter[] players = null)
		{
			if (PlayerInstances != null || EnemyInstances != null)
				return;

			// Use debug players and enemies if not set
			if (players == null || players.Length == 0)
			{
				players = _debugPlayers;
				Debug.LogWarning(": Debug players :");
			}

			if (enemies == null || enemies.Length == 0)
			{
				enemies = _debugEnemies;
				Debug.LogWarning(": Debug enemies :");
			}

			// boss fight check
			foreach (CombatCharacter cc in enemies)
			{
				if (cc.Info.characterClass == CharacterClass.Boss)
				{
					if (!IsBossFight) IsBossFight = true;
					else Debug.LogError("Too many bosses! One combat should have only one boss!");
				}
			}

			// create and place character instances to layouts
			PlayerInstances = new CombatCharacter[players.Length];
			EnemyInstances = new CombatCharacter[enemies.Length];


			CombatLayout[] combatLayouts = GetComponentsInChildren<CombatLayout>(includeInactive: true);
			foreach (CombatLayout cl in combatLayouts)
			{
				if (cl.team == CombatLayout.Team.Player && (int)cl.layout == players.Length)
					InitAndSpawnCharactersToLayout(cl.transform, players);
				else if (cl.team == CombatLayout.Team.Boss && (int)cl.bossLayout == enemies.Length - 1 && IsBossFight)
					InitAndSpawnCharactersToLayout(cl.transform, enemies);
				else if (cl.team == CombatLayout.Team.Enemy && (int)cl.layout == enemies.Length && !IsBossFight)
					InitAndSpawnCharactersToLayout(cl.transform, enemies);

				cl.gameObject.SetActive(cl.GetComponentInChildren<CombatCharacter>());
			}

			foreach (CombatCharacter cc in PlayerInstances)
			{
				if (!cc.IsAlive) continue;

				cc.transform.position = new Vector3(_combatEnterSpot.position.x, cc.transform.parent.position.y,
					cc.transform.parent.position.z);
			}
		}

		public void BeginCombat(CombatCharacter[] enemies = null, CombatCharacter[] players = null)
		{
			// InputState is set by SceneManager
			//Set cameras priority values
			//_combatCharacterCamera.Priority = Mathf.FloorToInt(Camera.main.depth) - 1;
			//_combatActionCamera.Priority = Mathf.FloorToInt(Camera.main.depth) - 1;

			_startCombat = StartCoroutine(WaitToBeginCombat(enemies, players));
		}

		IEnumerator WaitToBeginCombat(CombatCharacter[] enemies = null, CombatCharacter[] players = null)
		{
			LoadCombatCharacterInstances(enemies, players);


			// players run to combat
			yield return StartCoroutine(MovePlayersToPosition(MovePlayersType.CombatEnter));

			//Set UI active
			EnableCombatUI(true);
			EnableEndUI(false);

			InitPlayerStatUIs(PlayerInstances.Length);

			// full ordered character instance list
			_orderedByTurnSpeed.AddRange(PlayerInstances);
			_orderedByTurnSpeed.AddRange(EnemyInstances);
			_orderedByTurnSpeed = OrderBySpeed(_orderedByTurnSpeed, SpeedType.TurnSpeed);

			// turn order
			//_turnOrder = CalculateTurnOrder();
			// TODO: Consider cloning the list instead of calculating it twice
			_currentTurnOrder = CalculateTurnOrder();
			_nextTurnOrder = CalculateTurnOrder();

			// start turn order UI and first turn
			StartTurnOrderListAnimation(true);

			StopCoroutine(_startCombat);
			_startCombat = null;
		}

		void InitAndSpawnCharactersToLayout(Transform layout, CombatCharacter[] characters)
		{
			// hide editor sprites
			SpriteRenderer[] layoutSprites = layout.GetComponentsInChildren<SpriteRenderer>();
			foreach (SpriteRenderer sprite in layoutSprites)
				sprite.enabled = false;

			layout.gameObject.SetActive(true);

			//Spawn characters to correct positions
			int spaceForBoss = IsBossFight ? 1 : 0;
			for (int i = 0; i < layout.childCount; i++)
			{
				if (characters[i].Info.characterClass == CharacterClass.Boss)
				{
					EnemyInstances[0] = Instantiate(characters[i], layout.GetChild(0));
					EnemyInstances[0].Init();
					EnemyInstances[0].Index = 0;
					spaceForBoss = 0;
				}
				else if (characters[i].Info.characterClass == CharacterClass.Enemy)
				{
					EnemyInstances[i + spaceForBoss] = Instantiate(characters[i], layout.GetChild(i + spaceForBoss));
					EnemyInstances[i + spaceForBoss].Init();
					EnemyInstances[i + spaceForBoss].Index = i + spaceForBoss;
				}
				else
				{
					PlayerInstances[i] = Instantiate(characters[i], layout.GetChild(i));
					PlayerInstances[i].Init();
					PlayerInstances[i].PlayerIndex = i;
				}
			}
		}

		#endregion

		#region animations

		/// <summary>
		/// Move the character from current position to target position
		/// </summary>
		/// <param name="cc">Character to move</param>
		/// <param name="target">Target position</param>
		/// <param name="duration">Duration of animation</param>
		/// <param name="parameterName">Name of animator bool parameter</param>
		/// <param name="parameterEndValue">Animator parameter's end value after animation completed</param>
		/// <returns></returns>
		IEnumerator MoveCharacterToPosition(CombatCharacter cc, Vector3 target, float duration,
			string parameterName = null, bool parameterEndValue = false)
		{
			//Animation for moving character
			if (parameterName != null)
				cc.spriteAnimator.SetBool(parameterName, true);

			Vector3 initialPosition = cc.transform.position;

			//Move character to correct position
			float timeElapsed = 0f, time;
			while (timeElapsed < duration)
			{
				time = timeElapsed / duration;
				time = time * time * (3f - 2f * time);

				cc.transform.position = Vector3.Lerp(initialPosition, target, time);

				timeElapsed += Time.deltaTime;
				yield return null;
			}

			//Set animator value after animation completed
			if (parameterName != null)
				cc.spriteAnimator.SetBool(parameterName, parameterEndValue);
		}

		/// <summary>
		/// Move all players (for entering or exiting combat)
		/// </summary>
		/// <param name="type">Type of movement</param>
		/// <returns></returns>
		IEnumerator MovePlayersToPosition(MovePlayersType type)
		{
			_movePlayers.Clear();
			switch (type)
			{
				case MovePlayersType.CombatEnter:
					// decrease duration here to decrease the time it takes to get to combat.
					foreach (CombatCharacter cc in PlayerInstances)
					{
						if (!cc.IsAlive) continue;

						cc.transform.position = new Vector3(_combatEnterSpot.position.x, cc.transform.parent.position.y,
							cc.transform.parent.position.z);
						_movePlayers.Add(StartCoroutine(MoveCharacterToPosition(
							cc: cc,
							target: cc.transform.parent.position,
							duration: /*Random.Range(_minGroupMoveDuration, _maxGroupMoveDuration)*/
							_minGroupMoveDuration, // testing to just put move duration to the min specified in the inspector of combatmanager.
							parameterName: AnimatorExtension.ANIMATOR_IS_MOVING_TO_COMBAT)));
					}

					break;
				case MovePlayersType.CombatExit:
					foreach (CombatCharacter cc in PlayerInstances)
					{
						if (!cc.IsAlive) continue;

						_movePlayers.Add(StartCoroutine(MoveCharacterToPosition(
							cc: cc,
							target: new Vector3(_combatEnterSpot.position.x, cc.transform.parent.position.y,
								cc.transform.parent.position.z),
							duration: Random.Range(_minGroupMoveDuration, _maxGroupMoveDuration),
							parameterName: AnimatorExtension.ANIMATOR_IS_FLEEING)));
					}

					break;
				default:
					break;
			}

			foreach (Coroutine move in _movePlayers)
				yield return move;

			foreach (Coroutine move in _movePlayers)
				StopCoroutine(move);

			_movePlayers.Clear();
		}

		#endregion

		#region Turn order UI animations

		/// <summary>
		/// Animate the list of turn icons
		/// </summary>
		/// <param name="firstTime">Determine if this is the first call of the scene - First animation icons move from fade state</param>
		void StartTurnOrderListAnimation(bool firstTime = false)
		{
			//Set icon color to normal color
			_activeCharacterGlow.color = Color.white;
			_activeCharacterIcon.color = Color.clear;

			//Reset saved list of icons
			foreach (CombatCharacter character in _orderedByTurnSpeed)
				character.CurrentTurnOrderIcon = null;

			for (int i = 0; i < _currentTurnOrderEvents.Count; i++)
			{
				//Each character can only have one current order icon
				if (i < _currentTurnOrder.Count)
				{
					CombatCharacter currentChar = _currentTurnOrder[i];
					if (currentChar.CurrentTurnOrderIcon != null)
						break;

					_visibleIconCount++;

					//Set the icon values based on values specified by character
					currentChar.CurrentTurnOrderIcon = _currentTurnOrderEvents[i];
					_currentTurnOrderEvents[i].dead = false;
					_currentTurnOrderEvents[i].animator.SetLayerWeight((int)LayerID.Health, 0f);
					_currentTurnOrderEvents[i].glow.color = currentChar.Info.turnOrderGlow;
					_currentTurnOrderEvents[i].icon.sprite = currentChar.Info.turnOrderIcon;
					_currentTurnOrderEvents[i].icon.rectTransform.anchoredPosition = currentChar.Info.iconPosition;
					_currentTurnOrderEvents[i].icon.rectTransform.sizeDelta = currentChar.Info.iconSize;
				}

				//If first time animating icon list in scene the icons move from fade state
				if (firstTime)
					_currentTurnOrderEvents[i].animator
						.Play(i < _visibleIconCount ? StateName.ArriveFade.ToString() : StateName.ArriveHide.ToString(),
							0, 0f);
				else
					_currentTurnOrderEvents[i].animator
						.Play(i < _visibleIconCount ? StateName.Arrive.ToString() : StateName.ArriveHide.ToString(), 0,
							0f);

				_currentTurnOrderEvents[i].positionMultiplier = 1;
			}

			StartNextTurnOrderListAnimation();

			_turnOrderAnimationIsOver = false;

			if (firstTime) StartCharacterTurn();
		}

		/// <summary>
		/// Animate the icons after each turn - moving icons up or hiding icons of dead characters
		/// </summary>
		public void StartTurnOrderAnimation()
		{
			CombatCharacter currentChar;
			//Set the current active character icon values
			if (_turnIndex >= 0 && _turnIndex < _currentTurnOrder.Count)
			{
				// If _turnIndex is within the valid range, use _currentTurnOrder[_turnIndex]
				currentChar = _currentTurnOrder[_turnIndex];
				_activeCharacterGlow.color = currentChar.Info.turnOrderGlow;
				_activeCharacterIcon.color = Color.white;
				_activeCharacterIcon.sprite = currentChar.Info.turnOrderIcon;
				_activeCharacterIcon.rectTransform.anchoredPosition = currentChar.Info.iconPositionActive;
				_activeCharacterIcon.rectTransform.sizeDelta = currentChar.Info.iconSizeActive;
			}
			else if (_turnIndex - 1 >= 0 && _turnIndex - 1 < _currentTurnOrder.Count)
			{
				Debug.Log(" _turnIndex is out of range, but _turnIndex - 1 is within range, using _turnIndex - 1",
					this);
				// If _turnIndex is out of range, but _turnIndex - 1 is within range, use _currentTurnOrder[_turnIndex - 1]
				currentChar = _currentTurnOrder[_turnIndex - 1];
				_activeCharacterGlow.color = currentChar.Info.turnOrderGlow;
				_activeCharacterIcon.color = Color.white;
				_activeCharacterIcon.sprite = currentChar.Info.turnOrderIcon;
				_activeCharacterIcon.rectTransform.anchoredPosition = currentChar.Info.iconPositionActive;
				_activeCharacterIcon.rectTransform.sizeDelta = currentChar.Info.iconSizeActive;
			}
			else
			{
				// If both conditions fail, handle the invalid _turnIndex
				// _turnIndex is invalid (either negative or too large)
				Debug.LogWarning("Invalid _turnIndex encountered: " + _turnIndex);

				// Set currentChar to a default or null value
				currentChar = null;

				// Set default UI state
				_activeCharacterGlow.color = Color.clear;
				_activeCharacterIcon.color = Color.clear;
				_activeCharacterIcon.sprite = null;
				_activeCharacterIcon.rectTransform.anchoredPosition = Vector2.zero;
				_activeCharacterIcon.rectTransform.sizeDelta = Vector2.zero;
			}

			//Move icons
			_deadIconCount = 0;
			for (int i = 0; i < _currentTurnOrderEvents.Count; i++)
			{
				// skip dead icons
				if (_currentTurnOrderEvents[i].dead)
				{
					_deadIconCount++;
					continue;
				}

				// animate visible icons and set factor for animation movement
				if (i < _visibleIconCount + _deadIconCount)
				{
					_currentTurnOrderEvents[i].animator.Play(i - _deadIconCount == 0
						? StateName.Fade.ToString()
						: StateName.Move.ToString());
					_currentTurnOrderEvents[i].positionMultiplier = 1 + _deadIconCount;
				}
			}

			StartNextTurnOrderAnimation();
			_turnOrderAnimationIsOver = false;
		}

		/// <summary>
		/// Reset icons animator values after each turn
		/// </summary>
		public void EndTurnOrderAnimation()
		{
			//Decrease visible icon count after each turn
			_visibleIconCount--;

			for (int i = 0; i < _currentTurnOrderEvents.Count; i++)
			{
				_currentTurnOrderEvents[i].dead = false;
				_currentTurnOrderEvents[i].animator.SetLayerWeight((int)LayerID.Health, 0f);
				_currentTurnOrderEvents[i].animator
					.Play(i < _visibleIconCount ? StateName.Wait.ToString() : StateName.WaitHide.ToString(), 0, 0f);
			}


			for (int i = 0; i < _nextTurnOrderEvents.Count; i++)
			{
				_nextTurnOrderEvents[i].dead = false;
				_nextTurnOrderEvents[i].animator.SetLayerWeight((int)LayerID.Health, 0f);
				_nextTurnOrderEvents[i].animator
					.Play(i < _nextTurnVisibleIconCount ? StateName.Wait.ToString() : StateName.WaitHide.ToString(), 0,
						0f);
			}
		}

		/// <summary>
		/// Set the new list of turn order icons after each turn
		/// </summary>
		public void SetNewTurnOrderIcons()
		{
			foreach (CombatCharacter character in _orderedByTurnSpeed)
				character.CurrentTurnOrderIcon = null;

			int iconIndex = 0;
			//Set new icon values according to corresponding character
			for (int i = 0; i < _currentTurnOrderEvents.Count; i++)
			{
				if (iconIndex >= _visibleIconCount) break;

				int currentIndex = _turnIndex + iconIndex + 1;
				if (currentIndex >= _currentTurnOrder.Count)
					break;
				CombatCharacter targetChar = _currentTurnOrder[currentIndex];
				if (!targetChar.IsAlive) continue;
				targetChar.CurrentTurnOrderIcon = _currentTurnOrderEvents[i];
				_currentTurnOrderEvents[i].glow.color = targetChar.Info.turnOrderGlow;
				_currentTurnOrderEvents[i].icon.sprite = targetChar.Info.turnOrderIcon;
				_currentTurnOrderEvents[i].icon.rectTransform.anchoredPosition = targetChar.Info.iconPosition;
				_currentTurnOrderEvents[i].icon.rectTransform.sizeDelta = targetChar.Info.iconSize;
				_currentTurnOrderEvents[i].positionMultiplier = 1;
				_currentTurnOrderEvents[i].dead = false;

				iconIndex++;
			}

			for (int i = 0; i < _nextTurnOrderEvents.Count; i++)
			{
				if (i >= _nextTurnVisibleIconCount) break;

				//int currentIndex = _turnIndex + i + 1;
				if (i >= _nextTurnOrder.Count)
					break;
				CombatCharacter targetChar = _nextTurnOrder[i];
				if (!targetChar.IsAlive) continue;
				targetChar.NextTurnOrderIcon = _nextTurnOrderEvents[i];
				_nextTurnOrderEvents[i].glow.color = targetChar.Info.turnOrderGlow;
				_nextTurnOrderEvents[i].icon.sprite = targetChar.Info.turnOrderIcon;
				_nextTurnOrderEvents[i].icon.rectTransform.anchoredPosition = targetChar.Info.iconPosition;
				_nextTurnOrderEvents[i].icon.rectTransform.sizeDelta = targetChar.Info.iconSize;
				_nextTurnOrderEvents[i].positionMultiplier = 1;
				_nextTurnOrderEvents[i].dead = false;
			}

			_turnOrderAnimationIsOver = true;
		}

		/// <summary>
		/// Animate next turn order icons in new turns
		/// </summary>
		private void StartNextTurnOrderListAnimation()
		{
			// Reset character's next turn icon
			foreach (CombatCharacter character in _orderedByTurnSpeed)
				character.NextTurnOrderIcon = null;

			_nextTurnVisibleIconCount = 0;

			// Ensure we don't try to access beyond the bounds of the _nextTurnOrderEvents or _nextTurnOrder
			int minCount = Mathf.Min(_nextTurnOrder.Count, _nextTurnOrderEvents.Count);

			// Set icon values for alive characters
			for (int i = 0; i < minCount; i++)
			{
				CombatCharacter currentChar = _nextTurnOrder[i];

				if (!currentChar.IsAlive) continue;

				_nextTurnOrderEvents[i].dead = false;
				currentChar.NextTurnOrderIcon = _nextTurnOrderEvents[i];
				_nextTurnOrderEvents[i].glow.color = currentChar.Info.turnOrderGlow;
				_nextTurnOrderEvents[i].icon.sprite = currentChar.Info.turnOrderIcon;
				_nextTurnOrderEvents[i].icon.rectTransform.anchoredPosition = currentChar.Info.iconPosition;
				_nextTurnOrderEvents[i].icon.rectTransform.sizeDelta = currentChar.Info.iconSize;
				_nextTurnOrderEvents[i].animator.Play(StateName.ArriveFade.ToString());
				_nextTurnOrderEvents[i].positionMultiplier = 1;

				_nextTurnVisibleIconCount++;
			}

			// Hide excess icons after setting valid ones
			for (int i = _nextTurnVisibleIconCount; i < _nextTurnOrderEvents.Count; i++)
			{
				_nextTurnOrderEvents[i].dead = true;
				_nextTurnOrderEvents[i].animator.Play(StateName.ArriveHide.ToString());
				_nextTurnOrderEvents[i].glow.color = Color.clear; // Optional: reset glow color
				_nextTurnOrderEvents[i].icon.sprite = null; // Optional: clear the sprite
				_nextTurnOrderEvents[i].icon.rectTransform.sizeDelta = Vector2.zero;
			}
		}

		/// <summary>
		/// Animate next turn icons when changes appear (dead character)
		/// </summary>
		void StartNextTurnOrderAnimation()
		{
			_deadIconCount = 0;
			int deadIconIndex = 0;
			for (int i = 0; i < _nextTurnOrderEvents.Count; i++)
			{
				// skip dead icons
				if (_nextTurnOrderEvents[i].dead)
				{
					_deadIconCount++;
					deadIconIndex = i;
					_nextTurnOrderEvents[i].animator.Play(StateName.Fade.ToString());
					continue;
				}

				// animate visible icons and set factor for animation movement
				if (i < _nextTurnOrder.Count + _deadIconCount)
				{
					if (i > deadIconIndex && _deadIconCount != 0)
					{
						_nextTurnOrderEvents[i].animator.Play(StateName.Move.ToString());
						_nextTurnOrderEvents[i].positionMultiplier = _deadIconCount;
					}
				}
			}
		}

		#endregion

		#region turn order

		/// <summary>
		/// Orders given list by turn speed. Highest to lowest.
		/// </summary>
		/// <param name="list">List to order</param>
		/// <returns>Re ordered list</returns>
		private List<CombatCharacter> OrderBySpeed(List<CombatCharacter> list, SpeedType speedType, float turnSpeed = 0)
		{
			list.Sort((a, b) =>
			{
				float speedA, speedB;

				switch (speedType)
				{
					case SpeedType.TurnSpeed:
						speedA = a.CurrentTurnSpeed;
						speedB = b.CurrentTurnSpeed;
						break;
					case SpeedType.TotalSpeedModule:
						speedA = a.TotalSpeed % turnSpeed;
						speedB = b.TotalSpeed % turnSpeed;
						break;
					default:
						Debug.LogError("Unimplemented SpeedType");
						return 0;
				}

				// Sorting in descending order if you want the faster character first
				return speedB.CompareTo(speedA);
			});

			return list;
		}

		/// <summary>
		/// Calculates turn order
		/// </summary>
		/// <returns>New list of past, active and 50+ future turns.</returns>
		List<CombatCharacter> CalculateTurnOrder(List<Buff> previewBuffs = null)
		{
			_tempResultList.Clear();
			_tempTurnOrder.Clear(); // Added this here to "update" turn order more accurately?

			// Apply preview buffs (if any)
			if (previewBuffs != null)
				foreach (CombatCharacter cc in _targets)
				{
					if (!cc.IsAlive) continue;
					cc.AddPreviewBuffs(previewBuffs, StatType.Agility);
				}


			// Recalculate turn order based on speed and extra turns
			float maxTurnSpeed = 0;
			foreach (CombatCharacter cc in _orderedByTurnSpeed)
			{
				if (!cc.IsAlive) continue;

				maxTurnSpeed += cc.GetTurnSpeedNTurnsAhead(0);
				_tempTurnOrder.Add(cc);

				#region ExtraTurn Stuff

				// Add extra turns if the character has them
				for (int extra = 0; extra < cc.ExtraTurns; extra++)
				{
					_tempTurnOrder.Add(cc); // Adds extra turns for the character
				}

				#endregion

				// Calculate speed for the extra turns
				cc.TotalSpeed = cc.turnsStartedTotalSpeed.TryGetValue(cc.TurnsStartedPreview, out var value)
					? value
					: cc.GetTotalSpeedAtTurnsStarted();
			}

			// Sort the list by speed
			_tempResultList.AddRange(OrderBySpeed(_tempTurnOrder, SpeedType.TotalSpeedModule, turnSpeed: maxTurnSpeed));
			//foreach(CombatCharacter cc in _orderedByTurnSpeed)
			//{
			//	if(!cc.IsAlive)
			//		_tempResultList.Remove(cc);
			//}
			// Remove preview buffs after turn order is calculated
			if (previewBuffs != null)
				foreach (CombatCharacter cc in _orderedByTurnSpeed)
					cc.RemovePreviewBuffs(StatType.Agility);


			return new List<CombatCharacter>(_tempResultList);
		}

		// This is not really needed as it's called only if target has Agility Buff and we dont use buff system anymore.
		public void CalculateTurnOrderPreview()
		{
			//_turnOrderPreview.Clear();
			//_turnOrderPreview.AddRange(CalculateTurnOrder(ActiveCharacter.CombatAction.buffs));

			//WIP

			// turn order UI
			Debug.Log("TODO: TurnOrderPreview visuals", this);
		}

		// This is not really needed as it's called only if target has Agility Buff and we dont use buff system anymore.
		public void ApplyRecalculatedOrderToTurnOrder()
		{
			//WIP: reimplement using _currentTurnOrder and _nextTurnOrder


			//if (_turnOrderPreview.Count != 0)
			//{
			//    _turnOrder.Clear();

			//    for (int i = 0; i < _turnOrderPreview.Count; i++)
			//        _turnOrder.Add(_turnOrderPreview[i]);

			//    _turnOrderPreview.Clear();
			//}

			// Done:  reimplement using _currentTurnOrder and _nextTurnOrder not tested. Comment this out if bugged as this is not necessary needed?
			// Check if the _nextTurnOrder has any entries to apply
			if (_nextTurnOrder != null && _nextTurnOrder.Count > 0)
			{
				// Clear the _currentTurnOrder to prepare it for the new order
				_currentTurnOrder.Clear();

				// Copy each entry from _nextTurnOrder into _currentTurnOrder
				foreach (var turn in _nextTurnOrder)
				{
					_currentTurnOrder.Add(turn);
				}

				// Clear the _nextTurnOrder as it has been applied to _currentTurnOrder
				_nextTurnOrder.Clear();
			}
		}

		public void CharacterDefeated(CombatCharacter cc)
		{
			// Save character info
			if (Overworld.PlayerPartyManager.Instance)
			{
				Overworld.PlayerPartyManager.Instance.SetPlayerStats(cc, new List<ChangingStat>(cc.GetChangingStats()));
			}

			// Remove defeated character's turn from _currentTurnOrder
			if (_turnIndex >= 0 && _turnIndex < _currentTurnOrder.Count)
			{
				int index = _currentTurnOrder.FindIndex(_turnIndex, (character) => !character.IsAlive);
				if (index != -1)
				{
					_currentTurnOrder.RemoveAt(index);
					if (cc.CurrentTurnOrderIcon)
					{
						_visibleIconCount--;
					}
				}
			}
			else
			{
				//GetCurrentCharacter();
				//if (GetCurrentCharacter().CurrentTurnOrderIcon) // This can throw errors maybe needs error checking?
				//{
				//	_visibleIconCount--;
				//}
				CombatCharacter currentCharacter = GetCurrentCharacter();

				if (currentCharacter != null)
				{
					// Safely access currentCharacter's properties
					if (currentCharacter.CurrentTurnOrderIcon != null)
					{
						_visibleIconCount--;
					}
					else
					{
						Debug.LogWarning("CurrentTurnOrderIcon is null.");
					}
				}
				else
				{
					Debug.LogWarning("GetCurrentCharacter() returned null.");
				}

				Debug.LogWarning("incorrect Turnindex");
			}

			// Remove defeated character's turn from _nextTurnOrder
			int nextIndex = _nextTurnOrder.FindIndex((character) => !character.IsAlive);
			if (nextIndex != -1)
			{
				_nextTurnOrder.RemoveAt(nextIndex);
				if (cc.NextTurnOrderIcon)
				{
					_nextTurnVisibleIconCount--;
				}
			}

			StartCoroutine(RemoveTokens(cc));
			cc.ExtraTurns = 0;
			// Update turn order (if needed)
			// _turnOrder = CalculateTurnOrder();
			// _currentTurnOrder = CalculateTurnOrder();
			// _nextTurnOrder = CalculateTurnOrder();
		}

		public IEnumerator RemoveTokens(CombatCharacter cc)
		{
			yield return new WaitForSeconds(1);
			//cc.ccei.HideIcons(); // Remove tokens when dying.
			if (cc._info.characterClass != CharacterClass.Enemy && cc._info.characterClass != CharacterClass.Boss)
			{
				tokenUi.HideIconsPlayer(cc.PlayerIndex);
			}
			else
			{
				tokenUi.HideIcons(cc.Index);
			}
		}

		private CombatCharacter GetCurrentCharacter()
		{
			if (_turnIndex >= 0 && _turnIndex < _currentTurnOrder.Count)
			{
				return _currentTurnOrder[_turnIndex];
			}
			else if (_turnIndex - 1 >= 0 && _turnIndex - 1 < _currentTurnOrder.Count)
			{
				return _currentTurnOrder[_turnIndex - 1];
			}
			else
			{
				Debug.LogWarning("Invalid _turnIndex encountered: " + _turnIndex);
				// Set default UI state
				_activeCharacterGlow.color = Color.clear;
				_activeCharacterIcon.color = Color.clear;
				_activeCharacterIcon.sprite = null;
				_activeCharacterIcon.rectTransform.anchoredPosition = Vector2.zero;
				_activeCharacterIcon.rectTransform.sizeDelta = Vector2.zero;

				return null;
			}
		}

		public void CharacterRevived(CombatCharacter cc)
		{
			//Save character info
			if (Overworld.PlayerPartyManager.Instance)
				Overworld.PlayerPartyManager.Instance.SetPlayerStats(cc, new List<ChangingStat>(cc.GetChangingStats()));

			// update turn order
			_currentTurnOrder = CalculateTurnOrder();
			_nextTurnOrder = CalculateTurnOrder();

			Debug.Log("TODO: CharacterRevived visuals", this);
		}

		#endregion

		#region turn logic

		private void StartCharacterTurn()
		{
			if (_turnIndex >= 0 && _turnIndex < _currentTurnOrder.Count)
			{
				// If _turnIndex is within the valid range, use _currentTurnOrder[_turnIndex]
				ActiveCharacter = _currentTurnOrder[_turnIndex];
				ActiveCharacter.StartTurn();

				// Camera
				//_combatCharacterCamera.Priority = Mathf.FloorToInt(Camera.main.depth) - 1;
				// _combatCharacterCamera.LookAt = ActiveCharacter.transform;
			}
			else if (_turnIndex - 1 >= 0 && _turnIndex - 1 < _currentTurnOrder.Count)
			{
				Debug.Log(" _turnIndex is out of range, but _turnIndex - 1 is within range, using _turnIndex - 1",
					this);
				// If _turnIndex is out of range, but _turnIndex - 1 is within range, use _currentTurnOrder[_turnIndex - 1]
				ActiveCharacter = _currentTurnOrder[_turnIndex - 1];
				ActiveCharacter.StartTurn();

				// Camera
				//_combatCharacterCamera.Priority = Mathf.FloorToInt(Camera.main.depth) - 1;
				// _combatCharacterCamera.LookAt = ActiveCharacter.transform;
			}
			else
			{
				// Handle invalid _turnIndex
				Debug.LogWarning("Invalid _turnIndex encountered in StartCharacterTurn: " + _turnIndex);
				ActiveCharacter =
					null; // or some default/fallback behavior/maybe get some character so can continue combat
			}
		}

		public IEnumerator EnemyTurnLogic()
		{
			//Set action and target
			if (!ActiveCharacter.Stunned && ActiveCharacter.IsAlive) // Testing
			{
				ActiveCharacter.SetAction();
				// AttackSound Sound // Take damage or hit sound in combatcharacter takedamage method
				if (ActiveCharacter.CombatAction != null)
				{
					if (ActiveCharacter != null && ActiveCharacter.CombatAction != null)
					{
						if (ActiveCharacter.CombatAction.actionSound.IsNull)
						{
							Debug.Log("actionSound is null or invalid.", this);
						}
						else
						{
#if UNITY_EDITOR
							// .Path is editor only
							Debug.Log($"Playing sound: {ActiveCharacter.CombatAction.actionSound.Path}", this);
#endif
							FMODUnity.RuntimeManager.PlayOneShot(ActiveCharacter.CombatAction.actionSound);
						}
					}
					else
					{
						Debug.Log("ActiveCharacter or CombatAction is null.", this);
					}

					if (ActiveCharacter.CombatAction.ActionSound != null)
					{
						Debug.Log("Enemy did: " + ActiveCharacter.CombatAction.actionName, this);
						Audio.AudioManager.Instance.PlayActionSound(ActiveCharacter.CombatAction.ActionSound
							.GetComponent<FMODUnity.StudioEventEmitter>());
					}
				}

				yield return new WaitForSeconds(ActiveCharacter.Info.animationTarget);

				// Initialize a variable to hold the target index
				int targetIndex = -1;

				// Check for taunted characters
				for (int i = 0; i < PlayerInstances.Length; i++)
				{
					if (PlayerInstances[i].Taunt && PlayerInstances[i].IsAlive)
					{
						Debug.Log("Taunt target: " + PlayerInstances[i]._info.characterName, this);
						targetIndex = i;
						UpdateSingleTarget(PlayerInstances, targetIndex,
							0); // If taunted will get target from here otherwise will get it from SetAction()
						break; // Exit the loop once a taunted character is found
					}
				}


				if (ActiveCharacter.CombatAction.actionType == ActionType.Attack)
				{

				}
				else if (ActiveCharacter.CombatAction.actionType == ActionType.Spell)
				{

				}
				// Attack animation also check here if spell or attack and get spell anim from the action and pass it to the animatorextension also migrate this to above if attack currently just plays attack anim of enemies.
				if (AnimatorExtensions.HasParameter(ActiveCharacter.spriteAnimator._animator,
						AnimatorExtension.ANIMATOR_IS_ATTACKING))
				{
					ActiveCharacter.spriteAnimator.SetBoolForSeconds(AnimatorExtension.ANIMATOR_IS_ATTACKING, true,
						ActiveCharacter.spriteAnimator.attackDuration);
				}
				else
				{
					Debug.LogWarning($"Animator does not contain parameter: {AnimatorExtension.ANIMATOR_IS_ATTACKING}");
				}

				//Do action on target. 
				yield return new WaitForSeconds(ActiveCharacter.Info.animationAct);


				ActiveCharacter.ActOnTargets(_targets.ToArray());
				_targets.Clear();
				Debug.Log("Enemy end turn", this);
				yield return new WaitForSeconds(ActiveCharacter.Info.animationEndTurn);

				ActiveCharacter.EndTurn();
			}
			else // if stunned or dead do this
			{
				yield return new WaitForSeconds(ActiveCharacter.Info.animationEndTurn);
				ActiveCharacter.EndTurn();
			}


			yield return new WaitForSeconds(ActiveCharacter.Info.animationEndTurn);

			ActiveCharacter.EndTurn();
		}

		public void PlayAttackSound()
		{
			// Attack Sound on enemies done diffrently
			if (ActiveCharacter._combatAction.actionSound.IsNull == false)
			{
				FMODUnity.RuntimeManager.PlayOneShot(ActiveCharacter._combatAction.actionSound);
			}
		}

		public IEnumerator PlayerTurnLogic() // This is numerator to wait till the anim is done so char wont move back mid anim.
		{
			if (ActiveCharacter.Stunned || !ActiveCharacter.IsAlive)
			{
				// tokenUi.HideIconsPlayer(ActiveCharacter.PlayerIndex); JERE: Include this?
				yield return new WaitForSeconds(ActiveCharacter.Info.animationEndTurn);
				ActiveCharacter.EndTurn();
				yield break;
			}

			// update stats ui
			ActiveCharacter.HighlightStatUI();

			// Action camera method happens lower down in if statement for checking if targeting type is correct.


			// animate to combat spot
			_moveActivePlayer = StartCoroutine(MoveCharacterToPosition(
				cc: ActiveCharacter,
				target: _combatSpot.position,
				duration: _moveToActionDuration,
				parameterName: AnimatorExtension.ANIMATOR_IS_MOVING_TO_ACTION));
			yield return _moveActivePlayer;
			StopCoroutine(_moveActivePlayer);

			// show action menu // Migrated this below animate to combat spot to stop player doing other actions before reaching combat spot which could soft lock game.
			EnableActionMenu(ActiveCharacter.Info);
			Input.InputManager.InputState = Input.InputState.Combat;

			// wait for player to choose
			_waitingToAct = true;
			while (_waitingToAct)
				yield return null;

			//if (ActiveCharacter.CombatAction.actionType !=
			//    ActionType.Spell) // Comment this line out if want to still play normal attack sounds with Ability sounds at same time.
			//    PlayAttackSound();

			//If targetting enemies
			if (ActiveCharacter.CombatAction.targetingType == TargetingType.SingleEnemy ||
				ActiveCharacter.CombatAction.targetingType == TargetingType.AllEnemies ||
				ActiveCharacter.CombatAction.targetingType == TargetingType.EnemiesAndSelf ||
				ActiveCharacter.CombatAction.targetingType == TargetingType.SingleEnemyAndSelf)
			{
				if (ActiveCharacter.CombatAction.actionType == ActionType.Spell)
					Debug.LogError("Add vignette here", this);

				if (_targets.Count > 0)
				{
					foreach (CombatCharacter target in _targets)
					{
						target.DeActivateTargetingArrow(); // Here to deactivate targeting indicator sooner.
						foreach (CombatCharacter cc in EnemyInstances)
						{
							if (cc != null)
							{
								cc.DeactivateInfo();
							}
						}
					}
				}
			}

			// hide action menu
			CombatActionMenu.ChangeActiveMenu(CombatActionMenuType.Hide);
			Input.InputManager.InputState = Input.InputState.Ignore;

			// animate act
			if (_targets.Count > 0)
			{
				#region if want to add anims or sounds to spells etc.

				// This is commented but when there is a way or spells have diffrent anims then use this or just use it to play diffrent sound etc. also do this for enemies?
				if (ActiveCharacter.CombatAction.actionType ==
					ActionType
						.Spell) // This is commented but when there is a way or spells have diffrent anims then use this or just use it to play diffrent sound etc.
				{
					if (ActiveCharacter != null && ActiveCharacter.CombatAction != null)
					{
						if (ActiveCharacter.CombatAction.actionSound.IsNull)
						{
							Debug.Log("actionSound is null or invalid.", this);
							ActiveCharacter.spriteAnimator.SetBoolForSeconds(AnimatorExtension.ANIMATOR_IS_ATTACKING, true,
							   ActiveCharacter.spriteAnimator.attackDuration);
							yield return new WaitForSeconds(ActiveCharacter.spriteAnimator.attackDuration); // Do spell anims here.
						}
						else
						{
#if UNITY_EDITOR
							// .Path is editor only
							Debug.Log($"Playing sound: {ActiveCharacter.CombatAction.actionSound.Path}");
#endif
							FMODUnity.RuntimeManager.PlayOneShot(ActiveCharacter.CombatAction.actionSound); // Migrate this to animator extension to play sound with anim event

							// Check here in the future that if not spell do this.
							ActiveCharacter.spriteAnimator.SetBoolForSeconds(AnimatorExtension.ANIMATOR_IS_ATTACKING, true,
								ActiveCharacter.spriteAnimator.attackDuration);
							yield return new WaitForSeconds(ActiveCharacter.spriteAnimator.attackDuration); // Do spell anims here.
						}
					}
					else
					{
						Debug.Log("ActiveCharacter or CombatAction is null.");

						// Check here in the future that if not spell do this.
						ActiveCharacter.spriteAnimator.SetBoolForSeconds(AnimatorExtension.ANIMATOR_IS_ATTACKING, true,
							ActiveCharacter.spriteAnimator.attackDuration);
						yield return new WaitForSeconds(ActiveCharacter.spriteAnimator.attackDuration); // Do spell anims here.
					}
					// Either use this override stuff or just add new anims to the controller?. Check for correct anim from spell?
					//var overrideController = ActiveCharacter.AbilityAnimController.GetOverrideControllerByName(ActiveCharacter._info.characterName ,ActiveCharacter.CombatAction.actionName);
					//if (overrideController != null)
					//{
					//	ActiveCharacter.AbilityAnimController.Set(overrideController);
					//}
					//else
					//{
					//	Debug.LogWarning("No matching AnimatorOverrideController found for action name: " + ActiveCharacter._info.characterName + ActiveCharacter.CombatAction.actionName);
					//	Debug.LogWarning("Make sure to name the AnimatorOverrideController same as CharacterName + AbilityName");
					//}
				}
				else if (ActiveCharacter.CombatAction.actionType == ActionType.Attack) ////these are here for own animations and / or own sounds.
				{
					if (ActiveCharacter != null && ActiveCharacter.CombatAction != null)
					{
						if (ActiveCharacter.CombatAction.actionSound.IsNull)
						{
							Debug.Log("actionSound is null or invalid.", this);
							ActiveCharacter.spriteAnimator.SetBoolForSeconds(AnimatorExtension.ANIMATOR_IS_ATTACKING, true,
							   ActiveCharacter.spriteAnimator.attackDuration);
							yield return new WaitForSeconds(ActiveCharacter.spriteAnimator.attackDuration); // Do spell anims here.
						}
						else
						{
#if UNITY_EDITOR
							// .Path is editor only
							Debug.Log($"Playing sound: {ActiveCharacter.CombatAction.actionSound.Path}");
#endif
							FMODUnity.RuntimeManager.PlayOneShot(ActiveCharacter.CombatAction.actionSound); // Migrate this to animator extension to play sound with anim event

							// Check here in the future that if not spell do this.
							ActiveCharacter.spriteAnimator.SetBoolForSeconds(AnimatorExtension.ANIMATOR_IS_ATTACKING, true, ActiveCharacter.spriteAnimator.attackDuration);
							yield return new WaitForSeconds(ActiveCharacter.spriteAnimator.attackDuration);

						}
					}
					else
					{
						Debug.Log("ActiveCharacter or CombatAction is null.");
						// Check here in the future that if not spell do this.
						ActiveCharacter.spriteAnimator.SetBoolForSeconds(AnimatorExtension.ANIMATOR_IS_ATTACKING, true,
							ActiveCharacter.spriteAnimator.attackDuration);
						yield return new WaitForSeconds(ActiveCharacter.spriteAnimator.attackDuration);
					}
				}
				else if (ActiveCharacter.CombatAction.actionType == ActionType.Item) /*these are here for own animations and / or own sounds.*/
				{
					if (ActiveCharacter.spriteAnimator._animator.HasParameter("IsUsing"))
						ActiveCharacter.spriteAnimator.SetBoolForSeconds(AnimatorExtension.ANIMATOR_IS_USING, true, ActiveCharacter.spriteAnimator.useDuration);

					yield return new WaitForSeconds(ActiveCharacter.spriteAnimator.useDuration);

					Debug.Log("Using item");
				}

				//	Debug.Log("TODO: Animations to spells/Abilities also test if this works correctly when anims for abilities are done etccc.");
				//	// Get correct spell

				//	// TODO: ADD SPELL/ABILITIES TO COMBAT CHARACTER ANIMATION
				//	// Then take theirs duration and waitforthattime
				//	//yield return new WaitForSeconds(ActiveCharacter.spriteAnimator.attackDuration);	// TODO: Make that it's abilities animations duration

				//	// Maybe action camera start here??? as attack spell anim starts here.
				//}
				//else
				//{
				//	ActiveCharacter.spriteAnimator.SetBoolForSeconds(AnimatorExtension.ANIMATOR_IS_ATTACKING, true, ActiveCharacter.spriteAnimator.attackDuration);

				//	// Attack Sound on enemies done diffrently
				//	if(ActiveCharacter.attackSound != null)
				//	{
				//		Audio.AudioManager.Instance.PlayCombatSound(ActiveCharacter.attackSound);
				//	}
				//	yield return new WaitForSeconds(ActiveCharacter.spriteAnimator.attackDuration);
				//}

				#endregion

				#region SwitchCase instead of if elses
				//					switch (ActiveCharacter.CombatAction.actionType)
				//					{
				//						case ActionType.Spell:
				//							if (ActiveCharacter != null && ActiveCharacter.CombatAction != null)
				//							{
				//								if (ActiveCharacter.CombatAction.actionSound.IsNull)
				//								{
				//									Debug.Log("actionSound is null or invalid.", this);
				//								}
				//								else
				//								{
				//#if UNITY_EDITOR
				//									Debug.Log($"Playing sound: {ActiveCharacter.CombatAction.actionSound.Path}");
				//#endif
				//									FMODUnity.RuntimeManager.PlayOneShot(ActiveCharacter.CombatAction.actionSound);
				//									ActiveCharacter.spriteAnimator.SetBoolForSeconds(AnimatorExtension.ANIMATOR_IS_ATTACKING, true, ActiveCharacter.spriteAnimator.attackDuration);
				//								}
				//							}
				//							else
				//							{
				//								Debug.Log("ActiveCharacter or CombatAction is null.");
				//								ActiveCharacter.spriteAnimator.SetBoolForSeconds(AnimatorExtension.ANIMATOR_IS_ATTACKING, true, ActiveCharacter.spriteAnimator.attackDuration);
				//							}

				//							// Uncomment and set up if you add unique animations for spells.
				//							// var overrideController = ActiveCharacter.AbilityAnimController.GetOverrideControllerByName(
				//							//     ActiveCharacter._info.characterName, ActiveCharacter.CombatAction.actionName);
				//							// if (overrideController != null)
				//							// {
				//							//     ActiveCharacter.AbilityAnimController.Set(overrideController);
				//							// }
				//							// else
				//							// {
				//							//     Debug.LogWarning("No matching AnimatorOverrideController found.");
				//							// }

				//							break;

				//						case ActionType.Attack:
				//							if (ActiveCharacter != null && ActiveCharacter.CombatAction != null)
				//							{
				//								if (ActiveCharacter.CombatAction.actionSound.IsNull)
				//								{
				//									Debug.Log("actionSound is null or invalid.", this);
				//								}
				//								else
				//								{
				//#if UNITY_EDITOR
				//									Debug.Log($"Playing sound: {ActiveCharacter.CombatAction.actionSound.Path}");
				//#endif
				//									FMODUnity.RuntimeManager.PlayOneShot(ActiveCharacter.CombatAction.actionSound);
				//									ActiveCharacter.spriteAnimator.SetBoolForSeconds(AnimatorExtension.ANIMATOR_IS_ATTACKING, true, ActiveCharacter.spriteAnimator.attackDuration);
				//								}
				//							}
				//							else
				//							{
				//								Debug.Log("ActiveCharacter or CombatAction is null.");
				//								ActiveCharacter.spriteAnimator.SetBoolForSeconds(AnimatorExtension.ANIMATOR_IS_ATTACKING, true, ActiveCharacter.spriteAnimator.attackDuration);
				//							}
				//							break;

				//						case ActionType.Item:
				//							if (ActiveCharacter.spriteAnimator._animator.HasParameter("IsUsing"))
				//							{
				//								ActiveCharacter.spriteAnimator.SetBoolForSeconds(
				//									AnimatorExtension.ANIMATOR_IS_USING,
				//									true,
				//									ActiveCharacter.spriteAnimator.useDuration
				//								);
				//							}
				//							Debug.Log("Using item");
				//							break;

				//						default:
				//							Debug.LogWarning("Unknown action type: " + ActiveCharacter.CombatAction.actionType);
				//							break;
				//					}
				#endregion



				// Had attack sound method call here b4



				// MORE CAMERA STUFF REGARDING COMBAT ACTION
				//Time.timeScale = 1f;
				//CameraController.cameraBrain.DefaultBlend.Time = 0.5f;
				// TODO: MIGRATE THIS! TO DISPLAY NUMBERS ETC SOONER AND WITH ANIM
				//ActiveCharacter.ActOnTargets(_targets.ToArray());


				if (ActiveCharacter.CombatAction.actionType == ActionType.Spell)
					yield return new WaitForSeconds(0.5f);
				// yield return here to make action camera to not cut out that fast.
			}

			_targets.Clear();

			// animate back
			Debug.Log("Starting MoveCharacterToPosition back animation.");
			Debug.Log(ActiveCharacter.transform.parent.position);
			_moveActivePlayer = StartCoroutine(MoveCharacterToPosition(
				cc: ActiveCharacter,
				target: ActiveCharacter.transform.parent.position,
				duration: _moveToActionDuration));
			yield return _moveActivePlayer;
			Debug.Log("Finished MoveCharacterToPosition back animation.");
			StopCoroutine(_moveActivePlayer);
			CameraController.cameraBrain.DefaultBlend.Time = 0.5f; // Reset camera blend time to default.

			// JERE: This is always true
			if (ActiveCharacter._info.characterClass != CharacterClass.Enemy ||
				ActiveCharacter._info.characterClass != CharacterClass.Boss)
			{
				ActiveCharacter.spriteAnimator.SetBool(AnimatorExtension.ANIMATOR_IS_TARGETING, false);
				if (ActiveCharacter._info.characterClass ==
					CharacterClass.Lydia) // TODO: Find better way of doing this.
				{
					Transform child = ActiveCharacter.transform.GetChild(4).transform.GetChild(0);
					if (child == null)
					{
						Debug.Log("Effects not found");
					}
					else
					{
						var PS = child.gameObject.GetComponent<ParticleSystem>();
						var main = PS.main;
						main.loop = false;
						main.simulationSpeed = 1.5f;
						StopEffect(child);
					}
				}
			}

			yield return new WaitForSeconds(ActiveCharacter.Info.animationEndTurn);
			ActiveCharacter.EndTurn();
		}

		void StopEffect(Transform effect)
		{
			var PS = effect.gameObject.GetComponent<ParticleSystem>();
			while (PS.particleCount > 2 && effect.gameObject.activeInHierarchy != false)
			{
				return;
			}

			effect.gameObject.SetActive(false);
		}


		public void EndTurn()
		{
			doneETOA = false;
			doneSNTOI = false;
			doneSTOA = false;

			var currentCharacter = GetCurrentCharacter(); // TEST

			#region ExtraTurn Stuff

			// Handle Extra Turns
			if (currentCharacter.ExtraTurns > 0 && currentCharacter.IsAlive)
			{
				// Consume an extra turn
				currentCharacter
						.ExtraTurns
					--; // FIND A WAY OF RESETTING EXTRA TURNS TO IT'S START VALUE

				// Do not increment turn index (same character continues the next turn)
				_startTurn = StartCoroutine(WaitToStartTurn());
				return;
			}

			#endregion

			else
			{
				// Move to the next character in the turn order
				_turnIndex++;
			}

			currentCombatAction = null;
			CameraController.ChangeFOV(60); // Comment this out.
											//Update turn order
			if (_visibleIconCount <= 0)
			{
				_turnIndex = 0;
				_currentTurnOrder = _nextTurnOrder;
				_nextTurnOrder = CalculateTurnOrder();
			}

			_startTurn = StartCoroutine(WaitToStartTurn());
		}

		public void RemoveCharacterFromTurnOrder(CombatCharacter character)
		{
			// Remove the character from the current and next turn orders
			_currentTurnOrder.Remove(character);
			_nextTurnOrder.Remove(character);
		}

		IEnumerator WaitToStartTurn()
		{
			// If combat end set end UI active, else start new turn
			if (CheckCombatEnd())
			{
				yield return new WaitForSeconds(_delayBeforeEndUI);
				bool enemyDefeated = true;
				bool playerDefeated = true;

				//Get enemy defeat status
				foreach (CombatCharacter enemy in EnemyInstances)
				{
					if (enemy.IsAlive)
						enemyDefeated = false;
				}

				//Get player defeat status
				foreach (CombatCharacter player in PlayerInstances)
				{
					if (player.IsAlive)
						playerDefeated = false;
				}

				if (enemyDefeated)
					EndCombat(true);
				else if (playerDefeated)
					EndCombat(false);
			}
			else
			{
				// start turn order animations
				if (_visibleIconCount <= 0)
					StartTurnOrderListAnimation();
				else
					StartTurnOrderAnimation();

				// wait for turn order animations
				while (!_turnOrderAnimationIsOver)
					yield return null;

				StartCharacterTurn();
			}

			StopCoroutine(_startTurn);
			_startTurn = null;
		}

		#endregion

		#region combat end

		public void EndCombat(bool Won)
		{
			// Show end ui
			CombatEnded = true;

			if (Won)
			{
				EnableEndUI(true);
			}


			foreach (CombatCharacter enemy in EnemyInstances) // Reset cooldowns when combat ends.
			{
				foreach (CombatAction combatAction in enemy._info.spells)
				{
					combatAction.remainingCoolDown = 0;
				}
				if (enemy._info.characterClass == CharacterClass.Boss && Won)
				{
					Inventory.Inventory.Instance.AbilityUpgradePoints += 1;// Add tokens that can be used to upgrade spells and attacks.
				}
			}

			foreach (CombatCharacter player in
					 PlayerInstances) // Reset cooldowns when combat ends. // Remove this if want to cooldowns to go between diffrent combat scenes.
			{
				foreach (CombatAction combatAction in player._info.spells)
				{
					combatAction.remainingCoolDown = 0;
				}
			}

			//Input is set to ignore after each player turn
			//Set input back to combat to navigate end ui
			Input.InputManager.InputState = Input.InputState.Combat;

			if (Won)
			{
				CombatEndUI.SetCombatResult(PlayerInstances);
			}
			else
			{
				Scenes.SceneTransitManager.Instance.GameOver();
			}
		}

		bool CheckCombatEnd()
		{
			bool enemyDefeated = true;
			bool playerDefeated = true;

			//Get enemy defeat status
			foreach (CombatCharacter enemy in EnemyInstances)
			{
				if (enemy.IsAlive)
					enemyDefeated = false;
			}

			//Get player defeat status
			foreach (CombatCharacter player in PlayerInstances)
			{
				if (player.IsAlive)
					playerDefeated = false;
			}

			if (enemyDefeated)
				CombatWon = true;
			else if (playerDefeated)
				CombatWon = false;

			return enemyDefeated || playerDefeated;
		}

		public void ClearCombatData()
		{
			//Clear list and turn index
			_turnOrder.Clear();
			_currentTurnOrder.Clear();
			_nextTurnOrder.Clear();
			_orderedByTurnSpeed.Clear();
			_turnIndex = 0;

			//Clear target indexes
			_enemyTargetIndex = 0;
			_playerTargetIndex = 0;
			_targets.Clear();

			//Clear characters
			foreach (CombatCharacter cc in PlayerInstances)
			{
				if (cc != null)
				{
					if (Overworld.PlayerPartyManager.Instance)
						Overworld.PlayerPartyManager.Instance.SetPlayerStats(cc,
							new List<ChangingStat>(cc.GetChangingStats()));

					Destroy(cc.gameObject);
				}
			}

			foreach (CombatCharacter cc in EnemyInstances)
				if (cc != null)
					Destroy(cc.gameObject);

			PlayerInstances = null;
			EnemyInstances = null;
		}

		#endregion

		#region UI

		/// <summary>
		/// Enable or disable the end combat UI
		/// </summary>
		/// <param name="enabled"></param>
		void
			EnableEndUI(
				bool enabled) // This will throw errors if combat is "started" from somewhere else than CombatStarter
		{
			for (int i = 0; i < transform.childCount; i++)
				transform.GetChild(i).gameObject.SetActive(!enabled);

			_endUI.SetActive(enabled);
		}

		/// <summary>
		/// Enable or disable the combat UI
		/// </summary>
		/// <param name="enabled"></param>
		void EnableCombatUI(bool enabled)
		{
			foreach (Transform child in transform)
			{
				if (child.GetComponent<Canvas>())
					child.gameObject.SetActive(enabled);
			}
		}

		/// <summary>
		/// Set player stats UI information
		/// </summary>
		/// <param name="playerCount"></param>
		void InitPlayerStatUIs(int playerCount)
		{
			Transform parent = GetComponentInChildren<CombatStatUpdater>(includeInactive: true).transform.parent;
			for (int i = 0; i < parent.childCount; i++)
			{
				if (i < playerCount)
				{
					PlayerInstances[i].CombatStatUpdater = parent.GetChild(i).GetComponent<CombatStatUpdater>();
					PlayerInstances[i].CombatStatUpdater.NameText.text = PlayerInstances[i].Info.characterName;
					PlayerInstances[i].CombatStatUpdater.UpdateStatUI(PlayerInstances[i].Stats);
					PlayerInstances[i].CombatStatUpdater.SetHighlight(false, 1f);
				}

				parent.GetChild(i).gameObject.SetActive(i < playerCount);
			}
		}

		void EnableActionMenu(CombatCharacterInfo info)
		{
			// populate spell menu
			int spellCount = info.spells == null || info.spells.Count == 0 ? 0 : info.spells.Count;
			if (spellCount > 0)
			{
				PopulateCombatActionMenu(CombatActionMenu.transform.GetChild((int)CombatActionMenuType.Spells),
					info.spells.ToArray());
				CombatActionMenu.SetButtonState(CombatButtonType.Spell, ButtonState.Normal);
			}
			else
				CombatActionMenu.SetButtonState(CombatButtonType.Spell, ButtonState.Disabled);

			// populate combo menu
			int comboCount = info.combos == null || info.combos.Count == 0 ? 0 : info.combos.Count;
			if (comboCount > 0)
			{
				PopulateCombatActionMenu(CombatActionMenu.transform.GetChild((int)CombatActionMenuType.Combos),
					info.combos.ToArray());
				CombatActionMenu.SetButtonState(CombatButtonType.Combo, ButtonState.Normal);
			}
			else
				CombatActionMenu.SetButtonState(CombatButtonType.Combo, ButtonState.Disabled);

			// populate item menu
			CombatAction[] itemActions = GetItemActions();
			if (itemActions.Length > 0)
			{
				PopulateCombatActionMenu(CombatActionMenu.transform.GetChild((int)CombatActionMenuType.Items),
					itemActions);
				CombatActionMenu.SetButtonState(CombatButtonType.Item, ButtonState.Normal);
			}
			else
				CombatActionMenu.SetButtonState(CombatButtonType.Item, ButtonState.Disabled);

			// show combat action menu
			CombatActionMenu.ChangeActiveMenu(CombatActionMenuType.Action);
		}

		CombatAction[] GetItemActions()
		{
			List<UseItemAction> actions = new ();

			foreach (Inventory.Consumable consumable in Inventory.Inventory.Instance.GetItemsByType(Inventory.ItemType
						 .Consumable))
			{
				if (Inventory.Inventory.Instance.GetItemCount(consumable) <= 0)
					continue;

				UseItemAction itemAction = ScriptableObject.CreateInstance<UseItemAction>();
				itemAction.Item = consumable;
				itemAction.actionName = consumable.itemName;
				itemAction.shortDescription = consumable.shortDescription;
				itemAction.longDescription = consumable.longDescription;
				itemAction.targetingType = consumable.targetingType;
				itemAction.canTargetDead = consumable.CanTargetDead;
				itemAction.buffs = consumable.buffs;
				//itemAction.HasAgilityBuff = itemAction.HasAgilityBuff && consumable.CanCureDebuff;

				actions.Add(itemAction);
			}

			return actions.ToArray();
		}

		void PopulateCombatActionMenu(Transform menu, CombatAction[] actions)
		{
			for (int i = 0; i < actions.Length + menu.childCount; i++)
			{
				_tempButton = null;
				// need button for action
				if (i < actions.Length)
				{
					// get button
					if (i < menu.childCount)
					{
						_tempButton = menu.GetChild(i);
						// delete non button
						if (_tempButton.GetComponent<CombatButton>() == null)
						{
							Destroy(_tempButton.gameObject);
							_tempButton = null;
						}
					}

					// make button
					if (_tempButton == null)
						_tempButton = Instantiate(_combatButtonPrefab, menu).transform;

					// setup button
					_tempButton.gameObject.SetActive(true);
					_tempButton.GetComponent<CombatButton>().Init(actions[i]);
				}
				// hide extra buttons
				else if (i < menu.childCount)
				{
					menu.GetChild(i).gameObject.SetActive(false);
				}
				// leave loop before index is out of bounds
				else break;
			}
		}

		#endregion

		#region HelperMethods for Boss targeting

		public CombatCharacter GetTargetWithLowestHP(CombatCharacter[] instances)
		{
			CombatCharacter lowestHpCharacter = null;
			float lowestHp = float.MaxValue; // Set a high default value

			foreach (CombatCharacter character in instances)
			{
				// Get the current health from the character's stats
				ChangingStat healthStat = character.Stats[(int)StatType.Health] as ChangingStat;

				if (healthStat != null && healthStat.Value < lowestHp)
				{
					lowestHp = healthStat.Value;
					lowestHpCharacter = character;
				}
			}

			return lowestHpCharacter;
		}

		public CombatCharacter GetTargetWithSpecificToken(CombatCharacter[] instances, EffectType tokenType)
		{
			return instances.FirstOrDefault(character =>
				character.CurrentEffects.Any(effect => effect.EffectType == tokenType));
		}

		#endregion

		#region targeting

		public void StartTargeting(CombatAction combatAction)
		{
			foreach (CombatCharacter cc in EnemyInstances)
			{
				if (ActiveCharacter._info.characterClass is CharacterClass.Enemy or CharacterClass.Boss) continue;
				if (cc != null) cc.ActivateInfo();
			}

			currentCombatAction = combatAction;

			if (combatAction.actionType == ActionType.Attack &&
				ActiveCharacter._info.characterClass != CharacterClass.Enemy)
			{
				CombatActionMenu.ChangeActiveMenu(CombatActionMenuType.Hide);
				if (ActiveCharacter._info.characterClass != CharacterClass.Boss)
				{
					ActiveCharacter.spriteAnimator.SetBool(AnimatorExtension.ANIMATOR_IS_TARGETING, true);
				}


				if (ActiveCharacter._info.characterClass ==
					CharacterClass.Lydia) // TODO: Find better way of doing this.
				{
					Transform child = ActiveCharacter.transform.GetChild(4).transform.GetChild(0);
					if (child == null)
					{
						Debug.Log("Effects not found");
					}
					else
					{
						child.gameObject.SetActive(false);
						var PS = child.gameObject.GetComponent<ParticleSystem>();
						var main = PS.main;
						main.loop = true;
						main.simulationSpeed = 1f;
						child.gameObject.SetActive(true);
					}
				}
			}

			// token glow stuff just happens when player is "targeting".
			// set correct targets
			switch (combatAction.targetingType)
			{
				// starting from last index find first valid enemy
				case TargetingType.SingleEnemy:
					if (ActiveCharacter.IsEnemyOrBoss())
					{
						UpdateSingleTarget(EnemyInstances, Random.Range(0, EnemyInstances.Length), 1);
					}
					else
					{
						_enemyTargetIndex = UpdateSingleTarget(EnemyInstances, _enemyTargetIndex, 0);
						if (!ActiveCharacter.IsEnemyOrBoss())
						{
							CheckEnemiesHitActive();
							EnemiesTokenGlow(combatAction);
							CheckSelfAttackHitActive();
						}
					}

					break;
				case TargetingType.SinglePlayer: // On enemies get random index:)
					if (ActiveCharacter.IsEnemyOrBoss())
					{
						if (ActiveCharacter._info.characterClass == CharacterClass.Enemy)
							UpdateSingleTarget(PlayerInstances, Random.Range(0, PlayerInstances.Length), 1);
						else
							BossSingleTargetTargeting();
					}
					else
					{
						_playerTargetIndex = UpdateSingleTarget(PlayerInstances, _playerTargetIndex, 0);
						if (!ActiveCharacter.IsEnemyOrBoss())
						{
							PlayerTeamTokenGlow(combatAction);
							CheckPlayersHitActive();
						}
					}

					break;
				case TargetingType.AllEnemies:
					UpdateAllTargets(EnemyInstances);
					if (!ActiveCharacter.IsEnemyOrBoss())
					{
						EnemiesTokenGlow(combatAction);
						CheckEnemiesHitActive();
						CheckSelfAttackHitActive();
					}
					break;
				case TargetingType.AllPlayers:
					UpdateAllTargets(PlayerInstances);
					if (!ActiveCharacter.IsEnemyOrBoss())
					{
						PlayerTeamTokenGlow(combatAction);
						CheckPlayersHitActive();
					}
					break;
				case TargetingType.Self:
					UpdateTargetSelf();
					if (!ActiveCharacter.IsEnemyOrBoss())
						SelfTokenGlow(combatAction);
					break;
				case TargetingType.EnemiesAndSelf:
					UpdateAllTargets(EnemyInstances);
					if (!ActiveCharacter.IsEnemyOrBoss())
					{
						EnemiesTokenGlow(combatAction);
						CheckEnemiesHitActive();
						CheckSelfAttackHitActive();
						//SelfTokenGlow(combatAction);
					}

					break;
				case TargetingType.SingleEnemyAndSelf:
					if (ActiveCharacter.IsEnemyOrBoss())
					{
						UpdateSingleTarget(EnemyInstances, Random.Range(0, EnemyInstances.Length), 1);
					}
					else
					{
						_enemyTargetIndex = UpdateSingleTarget(EnemyInstances, _enemyTargetIndex, 0);
						if (!ActiveCharacter.IsEnemyOrBoss())
						{
							EnemiesTokenGlow(combatAction);
							CheckEnemiesHitActive();
							//SelfTokenGlow(combatAction);
							CheckSelfAttackHitActive();
						}
					}

					break;
				case TargetingType.AllPlayerAndSelf:
					UpdateAllTargets(PlayerInstances);
					if (!ActiveCharacter.IsEnemyOrBoss())
					{
						PlayerTeamTokenGlow(combatAction);
						CheckPlayersHitActive();
					}
					break;
				case TargetingType.SinglePlayerAndSelf:
					if (ActiveCharacter.IsEnemyOrBoss())
					{
						UpdateSingleTarget(PlayerInstances, Random.Range(0, PlayerInstances.Length), 1);
					}
					else
					{
						_playerTargetIndex = UpdateSingleTarget(PlayerInstances, _playerTargetIndex, 0);
						if (!ActiveCharacter.IsEnemyOrBoss())
						{
							PlayerTeamTokenGlow(combatAction);
							CheckPlayersHitActive();
						}

					}

					break;
				default:
					Debug.LogError("Unimplemented TargetingType");
					return;
			}


			_targetingType = combatAction.targetingType;
		}

		public void BossSingleTargetTargeting()
		{
			AI.BossCharacter bossCharacterScript = ActiveCharacter.gameObject.GetComponent<AI.BossCharacter>();
			if (bossCharacterScript != null)
			{
				if (bossCharacterScript.useLowestHealthTargeting &&
					bossCharacterScript.useSpecificTokenTargeting == false)
				{
					CombatCharacter lowestHpEnemy = GetTargetWithLowestHP(PlayerInstances);
					if (lowestHpEnemy != null)
					{
						UpdateSingleTarget(PlayerInstances, System.Array.IndexOf(PlayerInstances, lowestHpEnemy), 0);
					}
					else
					{
						UpdateSingleTarget(PlayerInstances, Random.Range(0, PlayerInstances.Length), 1);
					}
				}
				else if (bossCharacterScript.useSpecificTokenTargeting &&
						 bossCharacterScript.useLowestHealthTargeting ==
						 false) // If only use token targeting is true then use specific token targeting.
				{
					CombatCharacter targetWithToken =
						GetTargetWithSpecificToken(EnemyInstances, bossCharacterScript.tokenToTarget);
					if (targetWithToken != null)
					{
						UpdateSingleTarget(PlayerInstances, System.Array.IndexOf(PlayerInstances, targetWithToken), 0);
					}
					else
					{
						UpdateSingleTarget(PlayerInstances, Random.Range(0, PlayerInstances.Length), 1);
					}
				}
				else if (bossCharacterScript.useLowestHealthTargeting &&
						 bossCharacterScript.useSpecificTokenTargeting) // If both targeting is true then do this.
				{
					// Randomly decide which targeting method to prioritize (0 for token, 1 for health)
					int priority = Random.Range(0, 2);

					if (priority == 0) // Prioritize token targeting
					{
						CombatCharacter targetWithToken =
							GetTargetWithSpecificToken(EnemyInstances, bossCharacterScript.tokenToTarget);
						if (targetWithToken != null)
						{
							UpdateSingleTarget(PlayerInstances, System.Array.IndexOf(PlayerInstances, targetWithToken),
								0);
						}
						else // If no target with token, fallback to health targeting
						{
							CombatCharacter lowestHpEnemy = GetTargetWithLowestHP(PlayerInstances);
							if (lowestHpEnemy != null)
							{
								UpdateSingleTarget(PlayerInstances,
									System.Array.IndexOf(PlayerInstances, lowestHpEnemy), 0);
							}
							else
							{
								UpdateSingleTarget(PlayerInstances, Random.Range(0, PlayerInstances.Length), 1);
							}
						}
					}
					else // Prioritize health targeting
					{
						CombatCharacter lowestHpEnemy = GetTargetWithLowestHP(PlayerInstances);
						if (lowestHpEnemy != null)
						{
							UpdateSingleTarget(PlayerInstances, System.Array.IndexOf(PlayerInstances, lowestHpEnemy),
								0);
						}
						else // If no lowest HP target found, fallback to token targeting
						{
							CombatCharacter targetWithToken =
								GetTargetWithSpecificToken(EnemyInstances, bossCharacterScript.tokenToTarget);
							if (targetWithToken != null)
							{
								UpdateSingleTarget(PlayerInstances,
									System.Array.IndexOf(PlayerInstances, targetWithToken), 0);
							}
							else
							{
								UpdateSingleTarget(PlayerInstances, Random.Range(0, PlayerInstances.Length), 1);
							}
						}
					}
				}
				else if (bossCharacterScript.useSpecificTokenTargeting == false &&
						 bossCharacterScript.useLowestHealthTargeting ==
						 false) // If both are false then do targeting normally
				{
					UpdateSingleTarget(PlayerInstances, Random.Range(0, PlayerInstances.Length), 1);
				}
			}
			else // If bosscharacter script is null do targeting normally.
			{
				UpdateSingleTarget(PlayerInstances, Random.Range(0, PlayerInstances.Length), 1);
			}
		}

		#region Token glow stuff

		public void EnemiesTokenGlow(CombatAction combatAction)
		{
			foreach (CombatCharacter cc in EnemyInstances)
			{
				if (combatAction.effects != null)
				{
					foreach (Effect effect in combatAction.effects)
					{
						if (cc.negationRules.ContainsKey(effect.EffectValue.EffectType))
						{
							List<EffectType> negates = cc.negationRules[effect.EffectValue.EffectType];
							for (int i = cc.CurrentEffects.Count - 1; i >= 0; i--)
							{
								Effect existingEffect = cc.CurrentEffects[i];
								Debug.Log(existingEffect);
								if (negates.Contains(existingEffect.EffectType))
								{
									//cc.ccei.StartCoroutine(cc.ccei.IconGlowWhileTargeting(existingEffect));
									tokenUi.IconGlowWhileTargeting(existingEffect, false);
									Debug.Log("Glow negate");
								}
							}
						}
					}
				}
			}
		}

		public void CheckEnemiesHitActive()
		{
			foreach (CombatCharacter cc in EnemyInstances)
			{
				foreach (Effect effect in cc.CurrentEffects)
				{
					if (effect.hitsActive == 1 && effect.EffectCategory == EffectCategories.Defence)
					{
						tokenUi.IconGlowWhileTargeting(effect, false, true);
					}
				}
			}
		}

		public void CheckPlayersHitActive()
		{
			foreach (CombatCharacter cc in PlayerInstances)
			{
				foreach (Effect effect in cc.CurrentEffects)
				{
					if (effect.hitsActive == 1 && effect.EffectCategory == EffectCategories.Defence)
					{
						tokenUi.IconGlowWhileTargeting(effect, true, true);
					}
				}
			}
		}

		public void CheckSelfAttackHitActive() // Start glowing if "buff" token leaves after attacking.
		{
			foreach (Effect effect in ActiveCharacter.CurrentEffects)
			{
				if (effect.EffectCategory == EffectCategories.Attack)
				{
					if (effect.hitsActive == 1)
					{
						tokenUi.IconGlowWhileTargeting(effect, true, true);
					}
				}
			}
		}



		public void
			SelfTokenGlow(
				CombatAction combatAction) // This is not needed as we will change that there is no one char token glowing as it's better to glow on whole team to indicate negating.
		{
			if (combatAction.effects != null)
			{
				foreach (Effect effect in combatAction.effects)
				{
					if (ActiveCharacter.negationRules.ContainsKey(effect.EffectValue.EffectType))
					{
						List<EffectType> negates = ActiveCharacter.negationRules[effect.EffectValue.EffectType];
						for (int i = ActiveCharacter.CurrentEffects.Count - 1; i >= 0; i--)
						{
							Effect existingEffect = ActiveCharacter.CurrentEffects[i];
							Debug.Log(existingEffect);
							if (negates.Contains(existingEffect.EffectType))
							{
								//ActiveCharacter.ccei.StartCoroutine(ActiveCharacter.ccei.IconGlowWhileTargeting(existingEffect));
								if (ActiveCharacter._info.characterClass != CharacterClass.Enemy ||
									(ActiveCharacter._info.characterClass != CharacterClass.Boss))
									tokenUi.SelfTokenGlow(existingEffect, ActiveCharacter.PlayerIndex);
							}
						}
					}
				}
			}
		}

		public void PlayerTeamTokenGlow(CombatAction combatAction)
		{
			foreach (CombatCharacter cc in PlayerInstances)
			{
				if (combatAction.effects != null)
				{
					foreach (Effect effect in combatAction.effects)
					{
						if (cc.negationRules.ContainsKey(effect.EffectValue.EffectType))
						{
							List<EffectType> negates = cc.negationRules[effect.EffectValue.EffectType];
							for (int i = cc.CurrentEffects.Count - 1; i >= 0; i--)
							{
								Effect existingEffect = cc.CurrentEffects[i];
								Debug.Log(existingEffect);
								if (negates.Contains(existingEffect.EffectType))
								{
									//cc.ccei.StartCoroutine(cc.ccei.IconGlowWhileTargeting(existingEffect));
									tokenUi.IconGlowWhileTargeting(existingEffect, true);
								}
							}
						}
					}
				}
			}
		}

		#endregion

		/// <summary>
		/// This method is just mainly for when you end targeting then do something.
		/// </summary>
		public void StopTargeting()
		{
			tokenUi.StopGlowEffects();
			foreach (CombatCharacter cc in EnemyInstances)
			{
				if (cc != null)
				{
					cc.DeactivateInfo();
				}
			}

			_targets.Clear();
			if (ActiveCharacter._info.characterClass != CharacterClass.Enemy ||
				ActiveCharacter._info.characterClass != CharacterClass.Boss)
			{
				ActiveCharacter.spriteAnimator.SetBool(AnimatorExtension.ANIMATOR_IS_TARGETING, false);
				if (ActiveCharacter._info.characterClass ==
					CharacterClass.Lydia) // TODO: Find better way of doing this.
				{
					Transform child = ActiveCharacter.transform.GetChild(4).transform.GetChild(0);
					if (child == null)
					{
						Debug.Log("Effects not found");
					}
					else
					{
						var PS = child.gameObject.GetComponent<ParticleSystem>();
						var main = PS.main;
						main.loop = false;
						main.simulationSpeed = 1.5f;
						StopEffect(child);
					}
				}
			}


			//Set cameras priority
			//_combatCharacterCamera.Priority = Mathf.FloorToInt(Camera.main.depth) - 1;

			currentCombatAction = null;
		}

		public void ChangeTarget(Vector2 input)
		{
			int targetIndex = -1;

			// Check for taunted characters
			for (int i = 0; i < EnemyInstances.Length; i++)
			{
				if (EnemyInstances[i].Taunt && EnemyInstances[i].IsAlive)
				{
					Debug.Log("Taunt target: " + EnemyInstances[i]._info.characterName);
					targetIndex = i;
					_enemyTargetIndex = UpdateSingleTarget(EnemyInstances, targetIndex, 0);
					break; // Exit the loop once a taunted character is found
				}
			}

			// If no taunted character is found, let player select enemy.
			if (targetIndex == -1)
			{
				// up and down also added left and right
				if (input.y != 0)
				{
					if (_targetingType == TargetingType.SingleEnemy ||
						_targetingType == TargetingType.SingleEnemyAndSelf)
						_enemyTargetIndex =
							UpdateSingleTarget(EnemyInstances, _enemyTargetIndex, Mathf.RoundToInt(input.y));
					if (_targetingType == TargetingType.SinglePlayer ||
						_targetingType == TargetingType.SinglePlayerAndSelf)
						_playerTargetIndex = UpdateSingleTarget(PlayerInstances, _playerTargetIndex,
							Mathf.RoundToInt(input.y));
				}
				else if (input.x != 0)
				{
					if (_targetingType == TargetingType.SingleEnemy ||
						_targetingType == TargetingType.SingleEnemyAndSelf)
						_enemyTargetIndex =
							UpdateSingleTarget(EnemyInstances, _enemyTargetIndex, Mathf.RoundToInt(input.x));
					if (_targetingType == TargetingType.SinglePlayer ||
						_targetingType == TargetingType.SinglePlayerAndSelf)
						_playerTargetIndex = UpdateSingleTarget(PlayerInstances, _playerTargetIndex,
							Mathf.RoundToInt(input.x));
				}
			}
		}


		private int UpdateSingleTargetWithTaunt(CombatCharacter[] instances, int currentIndex, int startIndex)
		{
			int targetIndex = -1;

			// Check for taunted characters
			for (int i = 0; i < instances.Length; i++)
			{
				if (instances[i] != null && instances[i].Taunt)
				{
					targetIndex = i;
					break; // Exit the loop once a taunted character is found
				}
			}

			// If no taunted character is found, select the next valid target starting from the startIndex
			if (targetIndex == -1)
			{
				targetIndex = UpdateSingleTarget(instances, currentIndex, startIndex);
			}

			return targetIndex;
		}

		public int UpdateSingleTarget(CombatCharacter[] team, int currentIndex, int indexChange)
		{
			_targets.Clear();

			for (int i = 0; i < 10; i++)
			{
				// check for current target
				if (indexChange == 0) indexChange = 1;
				// check for next target
				else currentIndex += indexChange;

				// out of bounds corrections
				if (currentIndex == -1) currentIndex += team.Length;
				if (currentIndex == team.Length) currentIndex -= team.Length;
				// check for valid target
				if (team[currentIndex].IsAlive || ActiveCharacter.CombatAction.canTargetDead)
				{
					_targets.Add(team[currentIndex]);
					break;
				}
			}

			return currentIndex;
		}

		void UpdateAllTargets(CombatCharacter[] team)
		{
			_targets.Clear();

			foreach (CombatCharacter cc in team)
				_targets.Add(cc);
		}

		void UpdateTargetSelf()
		{
			_targets.Clear();
			_targets.Add(ActiveCharacter);
		}

		#endregion

		public void StartDefend()
		{
			StartCoroutine(Defend());
		}

		public IEnumerator Defend()
		{
			// Add a new buff to increase defense
			//ActiveCharacter.AddBuff(new Buff(StatType.PDefense, BuffType.Flat, 1, 10)); 
			//ActiveCharacter.AddBuff(new Buff(StatType.MDefense, BuffType.Flat, 1, 10));
			ActiveCharacter.AddBuff(new Buff(StatType.Agility, BuffType.Flat, 1, 5));
			CombatActionMenu.ChangeActiveMenu(CombatActionMenuType.Hide);
			Input.InputManager.InputState = Input.InputState.Ignore;

			// animate act
			if (_targets.Count > 0)
			{
				ActiveCharacter.spriteAnimator.SetBoolForSeconds(AnimatorExtension.ANIMATOR_IS_ATTACKING, true,
					ActiveCharacter.spriteAnimator.attackDuration);
				yield return new WaitForSeconds(ActiveCharacter.spriteAnimator.attackDuration);
				ActiveCharacter.ActOnTargets(_targets.ToArray());
			}

			_targets.Clear();

			// Set cameras priority
			//_combatActionCamera.Priority = Mathf.FloorToInt(Camera.main.depth) - 1;

			_moveActivePlayer = StartCoroutine(MoveCharacterToPosition(
				cc: ActiveCharacter,
				target: ActiveCharacter.transform.parent.position,
				duration: _moveToActionDuration));
			yield return _moveActivePlayer;
			StopCoroutine(_moveActivePlayer);

			ActiveCharacter.EndTurn();
		}


		#region flee

		public void TryToFlee()
		{
			_fleeCombat = StartCoroutine(FleeCombatLogic());
		}

		IEnumerator FleeCombatLogic()
		{
			// try to flee animation
			foreach (CombatCharacter cc in PlayerInstances)
				cc.spriteAnimator.SetBool(AnimatorExtension.ANIMATOR_IS_FLEEING, true);
			yield return new WaitForSeconds(1);
			// flee logic
			bool flee = true;
			// act
			if (flee)
			{
				ActiveCharacter.StopActiveTurn();
				StartCoroutine(MovePlayersToPosition(MovePlayersType.CombatExit));
				CombatEnded = true;
				ReturnToOverworld(true);
			}
			else
			{
				foreach (CombatCharacter cc in PlayerInstances)
					cc.spriteAnimator.SetBool(AnimatorExtension.ANIMATOR_IS_FLEEING, false);
				TryToAct();
			}

			StopCoroutine(_fleeCombat);
			_fleeCombat = null;
		}

		#endregion

		/// <returns>True when act is started</returns>
		public bool TryToAct()
		{
			if (_waitingToAct)
			{
				_waitingToAct = false;
				return true;
			}

			return false;
		}

		public void ReturnToOverworld(bool flee = false)
		{
			if (!CombatEnded)
				return;

			EnableCombatUI(false);
			ClearCombatData();

			CombatEnded = false;

			if (sceneToLoadAfterCombat != null)
			{
				Scenes.SceneTransitManager.Instance.ReturnToOverworld(sceneToLoadAfterCombat);
			}
			else
			{
				Scenes.SceneTransitManager.Instance.ReturnToOverworld();
			}


			// InputState is set by SceneManager
		}

		public bool KillAllEnemies()
		{
			if (_startCombat != null)
			{
				return false;
			}

			foreach (CombatCharacter enemy in EnemyInstances)
			{
				enemy.ChangeChangingStatValue(-enemy.Stats[(int)StatType.Health].Value);
			}

			CombatWon = true;
			EndCombat(true);
			return true;
		}

		public bool KillParty()
		{
			if (_startCombat != null)
			{
				return false;
			}

			foreach (CombatCharacter player in PlayerInstances)
			{
				player.ChangeChangingStatValue(-player.Stats[(int)StatType.Health].Value);
			}

			CombatWon = false;
			EndCombat(false);
			return true;
		}

		private void SetTargets(List<CombatCharacter> targets, ObservableListChangedEventArgs<CombatCharacter> e)
		{
			foreach (var target in e.Items)
			{
				target.IsTargeted = e.IsAddition;
			}

			if (targets.Count > 0 && ActiveCharacter.IsEnemyOrBoss())
			{
				CameraController.cameraBrain.DefaultBlend.Time = 0.2f;
				Debug.Log("Cut camera");
			}
		}
	}
}