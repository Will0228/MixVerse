using System;
using System.Collections.Generic;
using Minis;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MixVerse.Midi
{
    /// <summary>
    /// DJ コントローラーの入力を、ゲーム側が扱いやすい形に変換して公開する。
    /// Presenter が Minis に直接依存しないよう、ここで MIDI を吸収する。
    ///
    /// 割り当ては機材によって変わるため、すべて Inspector から変更できるようにしてある。
    /// 番号を調べたいときは MidiTester を使う。
    /// </summary>
    public sealed class DjControllerInput : MonoBehaviour
    {
        /// <summary>Minis は 0〜1 に正規化した値を渡すため、生の MIDI 値へ戻すのに使う。</summary>
        private const float MidiValueScale = 127.0f;

        /// <summary>フェーダーに触れるまでの向き。真ん中なので正面を向く。</summary>
        public const float DefaultFacingValue = 0.5f;

        [Header("MIDI Mapping")]
        [Tooltip("MIDI チャンネル。表示と同じ 1 始まりで指定する。")]
        [SerializeField] private int _midiChannel = 1;

        [Tooltip("手札選択の開始と確定に使うノート番号（左デッキの SYNC ボタン）。")]
        [SerializeField] private int _leftSyncNoteNumber = 64;

        [Tooltip("手札選択の開始と確定に使うノート番号（右デッキの SYNC ボタン）。")]
        [SerializeField] private int _rightSyncNoteNumber = 71;

        [Tooltip("拍手する手の呼び出しに使うノート番号（左デッキの CUE ボタン）。")]
        [SerializeField] private int _leftCueNoteNumber = 51;

        [Tooltip("拍手する手の呼び出しに使うノート番号（右デッキの CUE ボタン）。")]
        [SerializeField] private int _rightCueNoteNumber = 60;

        [Tooltip("カーソルを左右に動かすコントロールチェンジ番号（ジョグ）。")]
        [SerializeField] private int _jogControlNumber = 27;

        [Tooltip("この生の MIDI 値(0〜127)なら右へ、それ以外なら左へ動かす。")]
        [SerializeField] private int _jogRightRawValue = 1;

        [Header("Facing Fader")]
        [Tooltip("どちらを向くかを決めるフェーダーのコントロールチェンジ番号。")]
        [SerializeField] private int _facingControlNumber = 10;

        [Header("Cursor Knobs")]
        [Tooltip("CPU1 の照準を上下に動かすコントロールチェンジ番号（左デッキのツマミ）。")]
        [SerializeField] private int _leftCursorVerticalControlNumber = 27;

        [Tooltip("CPU1 の照準を左右に動かすコントロールチェンジ番号（左デッキのツマミ）。")]
        [SerializeField] private int _leftCursorHorizontalControlNumber = 28;

        [Tooltip("CPU2 の照準を上下に動かすコントロールチェンジ番号（右デッキのツマミ）。")]
        [SerializeField] private int _rightCursorVerticalControlNumber = 32;

        [Tooltip("CPU2 の照準を左右に動かすコントロールチェンジ番号（右デッキのツマミ）。")]
        [SerializeField] private int _rightCursorHorizontalControlNumber = 31;

        [Tooltip("この生の MIDI 値(0〜127)なら時計回り（下・右）、それ以外なら反時計回り（上・左）とみなす。")]
        [SerializeField] private int _cursorClockwiseRawValue = 1;

        [Header("Debug")]
        [SerializeField] private bool _logToConsole;

        private readonly Subject<DjDeckSide> _onSyncPressed = new Subject<DjDeckSide>();
        private readonly Subject<DjDeckSide> _onCuePressed = new Subject<DjDeckSide>();
        private readonly Subject<int> _onJogStep = new Subject<int>();
        private readonly Subject<DjCursorStep> _onCursorStep = new Subject<DjCursorStep>();
        private readonly ReactiveProperty<float> _facingValue = new ReactiveProperty<float>(DefaultFacingValue);

        private readonly Dictionary<MidiDevice, Handlers> _boundDevices = new Dictionary<MidiDevice, Handlers>();

        /// <summary>SYNC ボタンが押された。値は押されたデッキ（左右）。</summary>
        public Observable<DjDeckSide> OnSyncPressed => _onSyncPressed;

        /// <summary>CUE ボタンが押された。値は押されたデッキ（左右）。</summary>
        public Observable<DjDeckSide> OnCuePressed => _onCuePressed;

        /// <summary>スクラッチが回された。+1 が右、-1 が左。</summary>
        public Observable<int> OnJogStep => _onJogStep;

        /// <summary>照準用のツマミが回された。どちらのデッキかと画面上の移動方向を持つ。</summary>
        public Observable<DjCursorStep> OnCursorStep => _onCursorStep;

        /// <summary>
        /// どちらをどれだけ向いているかを表すフェーダーの値。
        /// 0.5 で正面、1 に近いほど左デッキ側、0 に近いほど右デッキ側を向く。
        /// フェーダーに触れるまでは 0.5 のまま。
        /// </summary>
        public ReadOnlyReactiveProperty<float> FacingValue => _facingValue;

        private sealed class Handlers
        {
            public Action<MidiNoteControl, float> NoteOn;
            public Action<MidiValueControl, float> ControlChange;
        }

        private void OnEnable()
        {
            foreach (var device in InputSystem.devices)
            {
                TryBind(device);
            }

            InputSystem.onDeviceChange += OnDeviceChange;
        }

        private void OnDisable()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;

            foreach (var pair in _boundDevices)
            {
                Unbind(pair.Key, pair.Value);
            }

            _boundDevices.Clear();
        }

        private void OnDestroy()
        {
            _onSyncPressed.Dispose();
            _onCuePressed.Dispose();
            _onJogStep.Dispose();
            _onCursorStep.Dispose();
            _facingValue.Dispose();
        }

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            switch (change)
            {
                case InputDeviceChange.Added:
                    TryBind(device);
                    break;

                case InputDeviceChange.Removed:
                    if (device is MidiDevice midi && _boundDevices.TryGetValue(midi, out var handlers))
                    {
                        Unbind(midi, handlers);
                        _boundDevices.Remove(midi);
                    }

                    break;
            }
        }

        private void TryBind(InputDevice device)
        {
            if (!(device is MidiDevice midi) || _boundDevices.ContainsKey(midi))
            {
                return;
            }

            // Minis はチャンネルごとに別デバイスを作るので、対象チャンネル以外は無視する
            if (midi.channel != _midiChannel - 1)
            {
                return;
            }

            var handlers = new Handlers
            {
                NoteOn = (note, velocity) => OnNoteOn(note.noteNumber, velocity),
                ControlChange = (control, value) => OnControlChange(control.controlNumber, value),
            };

            midi.onWillNoteOn += handlers.NoteOn;
            midi.onWillControlChange += handlers.ControlChange;

            _boundDevices.Add(midi, handlers);
        }

        private static void Unbind(MidiDevice midi, Handlers handlers)
        {
            midi.onWillNoteOn -= handlers.NoteOn;
            midi.onWillControlChange -= handlers.ControlChange;
        }

        private void OnNoteOn(int noteNumber, float velocity)
        {
            // Minis はベロシティ0を NoteOff として扱うため、ここに来た時点で押下とみなせる
            if (TryGetDeckSide(noteNumber, _leftSyncNoteNumber, _rightSyncNoteNumber, out var syncSide))
            {
                if (_logToConsole)
                {
                    Debug.Log($"[DJ] SYNC pressed ({syncSide}, note {noteNumber}, velocity {velocity:0.000})");
                }

                _onSyncPressed.OnNext(syncSide);
                return;
            }

            if (TryGetDeckSide(noteNumber, _leftCueNoteNumber, _rightCueNoteNumber, out var cueSide))
            {
                if (_logToConsole)
                {
                    Debug.Log($"[DJ] CUE pressed ({cueSide}, note {noteNumber}, velocity {velocity:0.000})");
                }

                _onCuePressed.OnNext(cueSide);
            }
        }

        /// <summary>
        /// ノート番号が左右どちらのデッキのボタンかを判定する。どちらでもなければ false。
        /// </summary>
        private static bool TryGetDeckSide(int noteNumber, int leftNoteNumber, int rightNoteNumber, out DjDeckSide side)
        {
            if (noteNumber == leftNoteNumber)
            {
                side = DjDeckSide.Left;
                return true;
            }

            if (noteNumber == rightNoteNumber)
            {
                side = DjDeckSide.Right;
                return true;
            }

            side = default;
            return false;
        }

        private void OnControlChange(int controlNumber, float value)
        {
            // 正規化された値を生の MIDI 値に戻して判定する。
            // ロータリーエンコーダは「右回りで 1、左回りで 127」のように
            // 値そのもので方向を表すことが多いため、しきい値ではなく一致で見る。
            var rawValue = Mathf.RoundToInt(value * MidiValueScale);

            // フェーダーは倒した位置そのものを送ってくるので、正規化された値をそのまま保持する
            if (controlNumber == _facingControlNumber)
            {
                var facing = Mathf.Clamp01(value);

                if (_logToConsole)
                {
                    Debug.Log($"[DJ] Facing cc{controlNumber} value={facing:0.000}");
                }

                _facingValue.Value = facing;
                return;
            }

            if (controlNumber == _jogControlNumber)
            {
                var step = rawValue == _jogRightRawValue ? 1 : -1;

                if (_logToConsole)
                {
                    Debug.Log($"[DJ] Jog cc{controlNumber} raw={rawValue} step={step}");
                }

                _onJogStep.OnNext(step);
            }

            // ジョグと照準のツマミに同じ番号を割り当てることもあるため、どちらも続けて判定する
            if (TryGetCursorStep(controlNumber, rawValue, out var cursorStep))
            {
                if (_logToConsole)
                {
                    Debug.Log($"[DJ] Cursor cc{controlNumber} raw={rawValue} deck={cursorStep.DeckSide} delta={cursorStep.Delta}");
                }

                _onCursorStep.OnNext(cursorStep);
            }
        }

        /// <summary>
        /// コントロールチェンジ番号が照準用のツマミかを判定し、画面上の移動方向へ変換する。
        /// 時計回りで下・右、反時計回りで上・左。どのツマミでもなければ false。
        /// </summary>
        private bool TryGetCursorStep(int controlNumber, int rawValue, out DjCursorStep cursorStep)
        {
            var isClockwise = rawValue == _cursorClockwiseRawValue;

            // 画面座標は上が +Y なので、時計回り（下移動）は -Y になる
            var vertical = new Vector2(0f, isClockwise ? -1f : 1f);
            var horizontal = new Vector2(isClockwise ? 1f : -1f, 0f);

            if (controlNumber == _leftCursorVerticalControlNumber)
            {
                cursorStep = new DjCursorStep(DjDeckSide.Left, vertical);
                return true;
            }

            if (controlNumber == _leftCursorHorizontalControlNumber)
            {
                cursorStep = new DjCursorStep(DjDeckSide.Left, horizontal);
                return true;
            }

            if (controlNumber == _rightCursorVerticalControlNumber)
            {
                cursorStep = new DjCursorStep(DjDeckSide.Right, vertical);
                return true;
            }

            if (controlNumber == _rightCursorHorizontalControlNumber)
            {
                cursorStep = new DjCursorStep(DjDeckSide.Right, horizontal);
                return true;
            }

            cursorStep = default;
            return false;
        }
    }
}
