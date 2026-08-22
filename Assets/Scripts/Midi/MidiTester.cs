using System;
using System.Collections.Generic;
using Minis;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MixVerse.Midi
{
    /// <summary>
    /// DJ コントローラー（MIDI）の動作確認用。
    /// 受信したメッセージを画面と Console に出し、どの操作子がどの番号を送るかを調べられるようにする。
    ///
    /// Minis は「MIDI チャンネルごとに1つの InputDevice」を作るため、
    /// 1台のコントローラーでも複数のデバイスとして現れることがある。
    /// </summary>
    public sealed class MidiTester : MonoBehaviour
    {
        [SerializeField] private bool _logToConsole = true;
        [SerializeField] private int _maxLogLines = 18;

        /// <summary>直近に受信したメッセージ（新しいものが先頭）。</summary>
        private readonly List<string> _logLines = new List<string>();

        /// <summary>操作子ごとの最新値。どのつまみが何番かを調べるために使う。</summary>
        private readonly Dictionary<string, string> _latestValues = new Dictionary<string, string>();

        private readonly List<string> _valueKeyOrder = new List<string>();

        /// <summary>購読解除できるようにデバイスごとのハンドラを保持する。</summary>
        private readonly Dictionary<MidiDevice, Handlers> _boundDevices = new Dictionary<MidiDevice, Handlers>();

        private GUIStyle _boxStyle;
        private GUIStyle _labelStyle;

        private sealed class Handlers
        {
            public Action<MidiNoteControl, float> NoteOn;
            public Action<MidiNoteControl> NoteOff;
            public Action<MidiValueControl, float> ControlChange;
        }

        private void OnEnable()
        {
            foreach (var device in InputSystem.devices)
            {
                TryBind(device);
            }

            InputSystem.onDeviceChange += OnDeviceChange;

            AddLog("MidiTester started. MIDI devices: " + _boundDevices.Count);
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
                        AddLog("Disconnected: " + midi.description.product);
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

            var handlers = new Handlers();

            // チャンネルはコントロール側ではなくデバイス側が持っている
            var channel = midi.channel;

            handlers.NoteOn = (note, velocity) =>
                Record("Note", channel, note.noteNumber, velocity.ToString("0.000"), "NoteOn");

            handlers.NoteOff = note =>
                Record("Note", channel, note.noteNumber, "0.000", "NoteOff");

            handlers.ControlChange = (control, value) =>
                Record("CC", channel, control.controlNumber, value.ToString("0.000"), "CC");

            midi.onWillNoteOn += handlers.NoteOn;
            midi.onWillNoteOff += handlers.NoteOff;
            midi.onWillControlChange += handlers.ControlChange;

            _boundDevices.Add(midi, handlers);

            AddLog("Connected: " + midi.description.product);
        }

        private static void Unbind(MidiDevice midi, Handlers handlers)
        {
            midi.onWillNoteOn -= handlers.NoteOn;
            midi.onWillNoteOff -= handlers.NoteOff;
            midi.onWillControlChange -= handlers.ControlChange;
        }

        /// <summary>
        /// 受信したメッセージをログと最新値の両方へ反映する。
        /// </summary>
        private void Record(string kind, int channel, int number, string value, string messageName)
        {
            // MIDI の慣習に合わせてチャンネルは 1 始まりで表示する
            var key = string.Format("{0} ch{1} #{2,-3}", kind, channel + 1, number);

            if (!_latestValues.ContainsKey(key))
            {
                _valueKeyOrder.Add(key);
            }

            _latestValues[key] = value;

            AddLog(string.Format("{0,-8} ch{1} #{2,-3} = {3}", messageName, channel + 1, number, value));
        }

        private void AddLog(string line)
        {
            _logLines.Insert(0, line);

            if (_logLines.Count > _maxLogLines)
            {
                _logLines.RemoveRange(_maxLogLines, _logLines.Count - _maxLogLines);
            }

            if (_logToConsole)
            {
                Debug.Log("[MIDI] " + line);
            }
        }

        private void OnGUI()
        {
            EnsureStyles();

            // 左: 受信したメッセージの流れ
            GUILayout.BeginArea(new Rect(10f, 10f, 430f, 420f), _boxStyle);
            GUILayout.Label("MIDI Messages   (devices: " + _boundDevices.Count + ")", _labelStyle);
            GUILayout.Space(4f);

            foreach (var line in _logLines)
            {
                GUILayout.Label(line, _labelStyle);
            }

            GUILayout.EndArea();

            // 右: 操作子ごとの最新値。つまみを動かすとどの番号かがすぐ分かる
            GUILayout.BeginArea(new Rect(450f, 10f, 330f, 420f), _boxStyle);
            GUILayout.Label("Latest value per control", _labelStyle);
            GUILayout.Space(4f);

            foreach (var key in _valueKeyOrder)
            {
                GUILayout.Label(key + " = " + _latestValues[key], _labelStyle);
            }

            GUILayout.EndArea();
        }

        private void EnsureStyles()
        {
            if (_boxStyle != null)
            {
                return;
            }

            _boxStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(10, 10, 10, 10) };
            _labelStyle = new GUIStyle(GUI.skin.label) { font = GUI.skin.font, fontSize = 13, richText = false };
        }
    }
}
