// Drop-in example: plays a Storyloom export with Unity UI (uGUI).
// 1. Export "Unity JSON" from Storyloom and put the file in Assets/Resources/ (e.g. the-lantern-road.unity.json).
// 2. Add this component to a GameObject; wire titleText, bodyText, speakerText, optionsParent, optionButtonPrefab.
// 3. Set storyResource to the file name WITHOUT ".json" (e.g. "the-lantern-road.unity").
//
// Swap the UI calls for TextMeshPro / UI Toolkit as you like — StoryRunner has no UI dependency.

using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Storyloom
{
    public class StoryloomExamplePlayer : MonoBehaviour
    {
        [Header("Story")]
        public string storyResource = "the-lantern-road.unity";

        [Header("UI")]
        public Text titleText;
        public Text speakerText;
        public Text bodyText;
        public RawImage image;
        public Transform optionsParent;
        public Button optionButtonPrefab;

        private StoryloomStory _story;
        private StoryRunner _runner;

        void Start()
        {
            var json = Resources.Load<TextAsset>(storyResource);
            if (json == null) { Debug.LogError($"Storyloom: Resources/{storyResource}.json not found."); return; }

            _story = StoryloomStory.FromJson(json.text);
            _runner = new StoryRunner(_story);
            _runner.OnNodeEntered += Render;
            _runner.OnEnding += n => Debug.Log($"Storyloom: reached ending '{n.title}'");
            _runner.OnVariableChanged += (name, value) => Debug.Log($"Storyloom: {name} = {value}");
            _runner.Start();
        }

        void Render(StoryNode node)
        {
            if (titleText) titleText.text = node.title;
            if (bodyText) bodyText.text = node.text;

            if (speakerText)
            {
                var who = _story.GetCharacter(node.speakerId);
                var where = _story.GetLocation(node.locationId);
                speakerText.text = string.Join("  ·  ", new[] { who?.name, where?.name, node.when }.Where(s => !string.IsNullOrEmpty(s)));
            }

            if (image)
            {
                var tex = StoryloomImages.ToTexture(node.image);
                image.texture = tex;
                image.gameObject.SetActive(tex != null);
            }

            // Rebuild option buttons
            if (optionsParent && optionButtonPrefab)
            {
                foreach (Transform c in optionsParent) Destroy(c.gameObject);
                foreach (var opt in _runner.GetOptions())
                {
                    var btn = Instantiate(optionButtonPrefab, optionsParent);
                    var label = btn.GetComponentInChildren<Text>();
                    if (label) label.text = opt.locked ? $"{opt.label}  (🔒 {opt.lockReason})" : opt.label;
                    btn.interactable = !opt.locked;
                    var captured = opt;
                    btn.onClick.AddListener(() => _runner.Choose(captured));
                }
            }
        }
    }
}
