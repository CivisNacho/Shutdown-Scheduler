using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Shutdown_Scheduler.Models;
using Windows.Devices.Midi;

namespace Shutdown_Scheduler.Services;

// Plays short chimes through the built-in Windows MIDI synthesizer. Failures are silent so an
// unavailable audio device never interrupts a schedule.
public sealed class MidiAlertSoundPlayer : IDisposable
{
    private const byte Channel = 0;
    private const byte Velocity = 90;

    private static readonly Dictionary<ScheduleAlertKind, Chime> Chimes = new()
    {
        // Two soft bell notes: a gentle heads-up.
        [ScheduleAlertKind.FiveMinutesBefore] = new Chime(11, new ChimeNote[]
        {
            new(79, 0, 500),
            new(84, 220, 700)
        }),
        // Three quicker marimba notes: the action is close.
        [ScheduleAlertKind.OneMinuteBefore] = new Chime(12, new ChimeNote[]
        {
            new(72, 0, 350),
            new(76, 180, 350),
            new(79, 360, 600)
        }),
        // Rising arpeggio resolving into a sustained C major chord: conditions are met.
        [ScheduleAlertKind.ConditionsMet] = new Chime(9, new ChimeNote[]
        {
            new(72, 0, 300),
            new(76, 130, 300),
            new(79, 260, 300),
            new(60, 390, 1500),
            new(72, 390, 1500),
            new(76, 390, 1500),
            new(79, 390, 1500),
            new(84, 390, 1500)
        })
    };

    private Task<MidiSynthesizer?>? _synthTask;
    private bool _isDisposed;

    public void Play(ScheduleAlertKind kind)
    {
        if (_isDisposed) return;
        _ = PlayAsync(Chimes[kind]);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_synthTask is { IsCompletedSuccessfully: true, Result: { } synth })
        {
            synth.Dispose();
        }
    }

    private async Task PlayAsync(Chime chime)
    {
        try
        {
            var synth = await GetSynthAsync().ConfigureAwait(false);
            if (synth is null || _isDisposed) return;

            synth.SendMessage(new MidiProgramChangeMessage(Channel, chime.Program));

            var messages = chime.Notes
                .SelectMany(note => new (int At, IMidiMessage Message)[]
                {
                    (note.StartMs, new MidiNoteOnMessage(Channel, note.Key, Velocity)),
                    (note.StartMs + note.DurationMs, new MidiNoteOffMessage(Channel, note.Key, 0))
                })
                .OrderBy(message => message.At);

            var elapsedMs = 0;
            foreach (var (at, message) in messages)
            {
                if (at > elapsedMs)
                {
                    await Task.Delay(at - elapsedMs).ConfigureAwait(false);
                    elapsedMs = at;
                }

                if (_isDisposed) return;
                synth.SendMessage(message);
            }
        }
        catch (Exception)
        {
            // The alert sound is best-effort; the schedule itself must keep running.
        }
    }

    private Task<MidiSynthesizer?> GetSynthAsync() => _synthTask ??= CreateSynthAsync();

    private static async Task<MidiSynthesizer?> CreateSynthAsync()
    {
        try
        {
            return await MidiSynthesizer.CreateAsync();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private sealed record Chime(byte Program, IReadOnlyList<ChimeNote> Notes);

    private readonly record struct ChimeNote(byte Key, int StartMs, int DurationMs);
}
