using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Hearthpulse.Core;
using Hearthpulse.Core.Cards;

namespace Hearthpulse.App.Infrastructure;

/// <summary>
/// The hero behind the main window's pages (<c>MainViewModel.PageHero</c>), cross-faded when a page changes it. Two
/// slots take turns, each the hero's colour wash (<see cref="CardArt"/> Glow) and its picture (Splash). The new hero
/// goes on the slot out of sight once its art is in memory, so the slot draws it whole at once, and the slots
/// cross-fade over <see cref="Motion.Enter"/>, started after the layout queued now, as the page's own fade is
/// (<see cref="Motion.AfterLayout"/>). Art not ready within <see cref="ArtMotion.HeroWait"/> (a download) lets the old
/// hero go to the stone meanwhile; the new one fades in when its art comes. Every change has a turn, so however fast
/// the pages change, the slots settle on the last hero. With motion off, or off screen, the hero changes at once.
/// </summary>
public sealed class HeroSwap
{
    private readonly Slot[] _slots;
    private readonly Func<bool> _motion;
    private int _front;
    private int _turn;
    private bool _asked;

    /// <param name="motion">Whether to animate; <see cref="CardArt.Moves"/> on the first wash unless a test says otherwise.</param>
    public HeroSwap(Border glowA, Border artA, Border glowB, Border artB, Func<bool>? motion = null)
    {
        _slots = [new Slot(glowA, artA), new Slot(glowB, artB)];
        _motion = motion ?? (() => CardArt.Moves(glowA));
        Rest(_slots[1], 0);
    }

    /// <summary>The hero asked for last (it may still be coming in).</summary>
    public string? Hero { get; private set; }

    /// <summary>The hero on the slot in front: the one shown, or coming in.</summary>
    internal string? Front => _slots[_front].Hero;

    /// <summary>The hero of each slot and its wash's opacity now (for tests).</summary>
    internal IReadOnlyList<(string? Hero, double Opacity)> Slots => [.. _slots.Select(s => (s.Hero, s.Glow.Opacity))];

    /// <summary>Shows <paramref name="hero"/> behind the page (null: none).</summary>
    public void Show(string? hero)
    {
        if (_asked && hero == Hero)
        {
            return;
        }

        _asked = true;
        Hero = hero;
        var turn = ++_turn;
        if (hero is null || !_motion())
        {
            Settle(hero);
            return;
        }

        SwapAsync(hero, turn).Forget("Page hero");
    }

    private async Task SwapAsync(string hero, int turn)
    {
        if (!CardArt.TryGetCached(ArtKind.Splash, hero, out _))
        {
            var load = CardArt.GetAsync(ArtKind.Splash, hero);
            if (await Task.WhenAny(load, Task.Delay(ArtMotion.HeroWait)) != load)
            {
                if (turn != _turn)
                {
                    return;
                }

                // Slow art (a download): the old hero goes to the stone meanwhile.
                foreach (var slot in _slots)
                {
                    Fade(slot, 0, () => Clear(slot, turn));
                }

                await load;
            }

            if (turn != _turn)
            {
                return;
            }
        }

        Motion.AfterLayout(_slots[0].Glow, () =>
        {
            if (turn == _turn)
            {
                CrossFade(hero, turn);
            }
        });
    }

    /// <summary>
    /// The slot already holding the hero comes back from where it is; else the fainter slot takes it, from transparent.
    /// The other slot fades out and lets its art go.
    /// </summary>
    private void CrossFade(string hero, int turn)
    {
        if (!_motion())
        {
            Settle(hero);
            return;
        }

        var incoming = _slots[_front].Hero == hero ? _front
            : _slots[1 - _front].Hero == hero ? 1 - _front
            : _slots[_front].Glow.Opacity <= _slots[1 - _front].Glow.Opacity ? _front : 1 - _front;
        var slot = _slots[incoming];
        if (slot.Hero != hero)
        {
            Rest(slot, 0);
            Set(slot, hero);
        }

        var outgoing = _slots[1 - incoming];
        _front = incoming;
        Fade(slot, 1);
        Fade(outgoing, 0, () => Clear(outgoing, turn));
    }

    /// <summary>At once: the front slot shows <paramref name="hero"/> whole, the other nothing.</summary>
    private void Settle(string? hero)
    {
        var front = _slots[_front];
        var back = _slots[1 - _front];
        Rest(back, 0);
        Set(back, null);
        Rest(front, 1);
        Set(front, hero);
    }

    /// <summary>A slot that faded out lets its art go, unless a later change uses it again.</summary>
    private void Clear(Slot slot, int turn)
    {
        if (turn == _turn && !ReferenceEquals(slot, _slots[_front]))
        {
            Set(slot, null);
        }
    }

    private static void Set(Slot slot, string? hero)
    {
        slot.Hero = hero;
        CardArt.SetGlow(slot.Glow, hero);
        CardArt.SetSplash(slot.Art, hero);
    }

    /// <summary>Fades both layers of the slot to <paramref name="to"/> from wherever they are, over <see cref="Motion.Enter"/>.</summary>
    private static void Fade(Slot slot, double to, Action? done = null)
    {
        foreach (var layer in slot.Layers)
        {
            var fade = new DoubleAnimation { To = to, Duration = Motion.Enter, EasingFunction = Motion.Ease };
            if (done is not null && layer == slot.Glow)
            {
                fade.Completed += (_, _) => done();
            }

            layer.BeginAnimation(UIElement.OpacityProperty, fade);
        }
    }

    /// <summary>Both layers of the slot at <paramref name="opacity"/>, no motion on them.</summary>
    private static void Rest(Slot slot, double opacity)
    {
        foreach (var layer in slot.Layers)
        {
            layer.BeginAnimation(UIElement.OpacityProperty, null);
            layer.Opacity = opacity;
        }
    }

    /// <summary>One slot: the wash and the picture of one hero.</summary>
    private sealed class Slot(Border glow, Border art)
    {
        public Border Glow { get; } = glow;

        public Border Art { get; } = art;

        public Border[] Layers { get; } = [glow, art];

        public string? Hero { get; set; }
    }
}
