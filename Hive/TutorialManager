using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

public class TutorialManager : MonoBehaviour
{

	[System.Serializable]
	public class Step
	{
		public string id;                  // Unique key, e.g. "pickup", created in the step list when adding new tutorials
		public GameObject panel;           // The Canvas / panel root to activvate 
		public PlayableDirector cutscene;  // Optional timeline
		[HideInInspector] public bool shown;   // Firsttime gate
		[HideInInspector] public int page;     // Current slide index
		[HideInInspector] public List<GameObject> slides = new List<GameObject>();
	}

	[SerializeField] private List<Step> steps = new List<Step>();

	private readonly Dictionary<string, Step> _lookup =
		new Dictionary<string, Step>();

	private Step _current;                                        // currently open
	public static TutorialManager Instance { get; private set; }

	public bool IsActive => _current != null;
	private static int _blockMenuFrame = -1;
	public static bool BlockMenuThisFrame => _blockMenuFrame == Time.frameCount;

	private const string TutorialSeenKeyPrefix = "TUT_SHOWN_"; // keep ids stable!
	private static string PrefKey(string id)
	{
				return TutorialSeenKeyPrefix + id;
	}


	void Awake()
	{
		if (Instance != null && Instance != this) { Destroy(gameObject); return; }
		Instance = this;
		DontDestroyOnLoad(gameObject);

		foreach (var s in steps)
		{
			if (!string.IsNullOrWhiteSpace(s.id) && !_lookup.ContainsKey(s.id))
				_lookup.Add(s.id, s);

			if (PlayerPrefs.GetInt(PrefKey(s.id), 0) == 1)
				s.shown = true;

			// Gather child slides (only active ones matter)
			foreach (Transform child in s.panel.transform)
				if (child.gameObject.activeSelf) s.slides.Add(child.gameObject);

			// Hide everything at boot
			s.panel.SetActive(false);
			foreach (var g in s.slides) g.SetActive(false);
		}
	}

	void Update()
	{
		if (_current == null) return;

		// Close
		if (Input.GetKeyDown(KeyCode.Escape))
		{
			HideCurrent();
			return;
		}

		// Navigation
		bool left =
			Input.GetKeyDown(KeyCode.A) ||
			Input.GetKeyDown(KeyCode.LeftArrow);

		bool right =
			Input.GetKeyDown(KeyCode.D) ||
			Input.GetKeyDown(KeyCode.RightArrow);

		if (left) ChangePage(-1);
		if (right) ChangePage(+1);
	}


	/// Show tutorial by id. call from any other script.
	public void Show(string id)
	{
		if (_current != null) return;
		if (!_lookup.TryGetValue(id, out var step) || step.shown) return;

		step.shown = true;
		PlayerPrefs.SetInt(PrefKey(step.id), 1);
		PlayerPrefs.Save(); // tiny write; okay to do here i think
		step.page = 0;
		_current = step;

		step.panel.SetActive(true);
		step.cutscene?.Play();
		ShowSlide(step, 0);

		Time.timeScale = 0f;          // Pause gameplay
	}

	public void HideCurrent()
	{
		if (_current == null) return;

		_blockMenuFrame = Time.frameCount;

		HideSlide(_current);          // hide last visible slide
		_current.panel.SetActive(false);
		_current.cutscene?.Stop();
		_current = null;

		Time.timeScale = 1f;          // Resume gameplay
	}

	void ChangePage(int delta)
	{
		if (_current.slides.Count <= 1) return;  // panel has no extra pages

		int next = Mathf.Clamp(
			_current.page + delta, 0, _current.slides.Count - 1);

		if (next == _current.page) return;       // hit either end

		HideSlide(_current);
		ShowSlide(_current, next);
	}

	static void ShowSlide(Step s, int index)
	{
		if (s.slides.Count == 0) return;         // safety
		s.page = index;
		s.slides[index].SetActive(true);
	}

	static void HideSlide(Step s)
	{
		if (s.slides.Count == 0) return;
		s.slides[s.page].SetActive(false);
	}
#if UNITY_EDITOR
	[ContextMenu("Reset All Tutorials")]
	public void ResetAllTutorials()
	{
		foreach (var s in steps)
		{
			s.shown = false;
			PlayerPrefs.DeleteKey(PrefKey(s.id));
		}
		PlayerPrefs.Save();
	}
#endif
}
