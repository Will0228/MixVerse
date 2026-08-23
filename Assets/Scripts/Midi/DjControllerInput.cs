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

        [Header("MIDI Mapping")]
        [Tooltip("MIDI チャンネル。表示と同じ 1 始まりで指定する。")]
        [SerializeField] private int _midiChannel = 1;

        [Tooltip("手札選択の開始と確定に使うノート番号（SYNC ボタン）。")]
        [SerializeField] private int _syncNoteNumber = 71;

        [Tooltip("拍手する手の呼び出しに使うノート番号（CUE ボタン）。")]
        [SerializeField] private int _cueNoteNumber = 67;

        [Tooltip("カーソルを左右に動かすコントロールチェンジ番号（ジョグ）。")]
        [SerializeField] private int _jogControlNumber = 27;

        [Tooltip("この生の MIDI 値(0〜127)なら右へ、それ以外なら左へ動かす。")]
        [SerializeField] private int _jogRightRawValue = 1;

        [Header("Debug")]
        [SerializeField] private bool _logToConsole;

        private readonly Subject<Unit> _onSyncPressed = new Subject<Unit>();
        private readonly Subject<Unit> _onCuePressed = new Subject<Unit>();
        private readonly Subject<int> _onJogStep = new Subject<int>();

        private readonly Dictionary<MidiDevice, Handlers> _boundDevices = new Dictionary<MidiDevice, Handlers>();

        /// <summary>SYNC ボタンが押された。</summary>
        public Observable<Unit> OnSyncPressed => _onSyncPressed;

        /// <summary>CUE ボタンが押された。</summary>
        public Observable<Unit> OnCuePressed => _onCuePressed;

        /// <summary>スクラッチが回された。+1 が右、-1 が左。</summary>
        public Observable<int> OnJogStep => _onJogStep;

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
            if (noteNumber == _syncNoteNumber)
            {
                if (_logToConsole)
                {
                    Debug.Log($"[DJ] SYNC pressed (note {noteNumber}, velocity {velocity:0.000})");
                }

                _onSyncPressed.OnNext(Unit.Default);
                return;
            }

            if (noteNumber == _cueNoteNumber)
            {
                if (_logToConsole)
                {
                    Debug.Log($"[DJ] CUE pressed (note {noteNumber}, velocity {velocity:0.000})");
                }

                _onCuePressed.OnNext(Unit.Default);
            }
        }

        private void OnControlChange(int controlNumber, float value)
        {
            if (controlNumber != _jogControlNumber)
            {
                return;
            }

            // 正規化された値を生の MIDI 値に戻して判定する。
            // ロータリーエンコーダは「右回りで 1、左回りで 127」のように
            // 値そのもので方向を表すことが多いため、しきい値ではなく一致で見る。
            var rawValue = Mathf.RoundToInt(value * MidiValueScale);
            var step = rawValue == _jogRightRawValue ? 1 : -1;

            if (_logToConsole)
            {
                Debug.Log($"[DJ] Jog cc{controlNumber} raw={rawValue} step={step}");
            }

            _onJogStep.OnNext(step);
        }
    }
}
