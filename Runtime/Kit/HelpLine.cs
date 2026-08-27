// Storyloom Unity Kit — on-screen key hint that follows the binds asset (and notices a gamepad being connected).
using UnityEngine;
using UnityEngine.UI;
namespace Storyloom
{
    public class HelpLine : MonoBehaviour
    {
        public Text text; float _t;
        void Update() { _t += Time.unscaledDeltaTime; if (_t < 1f) return; _t = 0; var d = StoryloomDirector.Instance; if (d && d.keys && text) { var p = StoryloomPlayer.Current; text.text = d.keys.HelpLine(p ? p.Style : GameStyle.TopDown); } }
    }
}
