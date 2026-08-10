//
// Copyright (C) 1993-1996 Id Software, Inc.
// Copyright (C) 2019-2020 Nobuaki Tanaka
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// The tic-command assembly is derived from managed-doom's src/Silk/SilkUserInput.cs, reading the phone's
// keyboard capability instead of a desktop window, and with the mouse paths removed.
//

using System;
using System.Collections.Generic;
using AetherOS.Sdk;
using ManagedDoom;
using ManagedDoom.UserInput;

namespace AetherOS.Apps.Doom.Backend;

/// <summary>Doom's input, driven by the phone's keyboard capability.
///
/// The engine wants input two different ways: menus consume discrete key EVENTS, while the world is driven
/// by a tic command polled once per tic. The capability only reports held state and press edges, so releases
/// are derived here by diffing against the previous frame; without that, a key would latch down inside the
/// engine the moment the player let go during a frame we did not observe.
///
/// There is no mouse. The phone's cursor belongs to the phone, and Doom shipped perfectly playable on the
/// keyboard alone, so the mouse members are inert rather than faked.</summary>
internal sealed class PhoneUserInput : IUserInput
{
    /// <summary>Keys the engine is told about as events. Movement is deliberately absent: it arrives through
    /// the tic command instead, and posting it twice would double every step.</summary>
    private static readonly (AppKey App, DoomKey Doom)[] EventKeys =
    [
        (AppKey.Up, DoomKey.Up),
        (AppKey.Down, DoomKey.Down),
        (AppKey.Left, DoomKey.Left),
        (AppKey.Right, DoomKey.Right),
        (AppKey.Enter, DoomKey.Enter),
        (AppKey.Escape, DoomKey.Escape),
        (AppKey.Tab, DoomKey.Tab),
        (AppKey.Y, DoomKey.Y),
        (AppKey.N, DoomKey.N),
        (AppKey.D1, DoomKey.Num1),
        (AppKey.D2, DoomKey.Num2),
        (AppKey.D3, DoomKey.Num3),
        (AppKey.D4, DoomKey.Num4),
        (AppKey.D5, DoomKey.Num5),
        (AppKey.D6, DoomKey.Num6),
        (AppKey.D7, DoomKey.Num7),
    ];

    private readonly IKeyboardInput keys;
    private readonly Config config;
    private readonly bool[] wasDown = new bool[EventKeys.Length];

    private int turnHeld;
    private float pendingTurn;

    /// <summary>Set while an on-screen control is held, so the touch pad and the keyboard drive the same
    /// tic command without either one clobbering the other.</summary>
    public TouchState Touch;

    public PhoneUserInput(Config config, IKeyboardInput keys)
    {
        this.config = config;
        this.keys = keys;
    }

    /// <summary>Adds horizontal drag, in screen pixels, to the turn owed to the next tic. Accumulated rather
    /// than sampled because several tics can run in one frame, and re-applying the same drag to each would
    /// spin the player by a multiple of what they actually dragged.</summary>
    public void AddMouseTurn(float deltaX) => this.pendingTurn += deltaX;

    /// <summary>Feeds menu key events to the engine. Called once per frame, before the tic is built.</summary>
    public void PumpEvents(global::ManagedDoom.Doom doom)
    {
        for (var i = 0; i < EventKeys.Length; i++)
        {
            var down = this.keys.IsDown(EventKeys[i].App);
            if (down == this.wasDown[i])
            {
                continue;
            }
            this.wasDown[i] = down;
            doom.PostEvent(new DoomEvent(down ? EventType.KeyDown : EventType.KeyUp, EventKeys[i].Doom));
        }
    }

    /// <summary>Releases everything the engine thinks is held. Used when the surface loses the keyboard, or
    /// the player would keep walking into a wall while the phone is in their pocket.</summary>
    public void ReleaseAll(global::ManagedDoom.Doom doom)
    {
        for (var i = 0; i < EventKeys.Length; i++)
        {
            if (!this.wasDown[i])
            {
                continue;
            }
            this.wasDown[i] = false;
            doom.PostEvent(new DoomEvent(EventType.KeyUp, EventKeys[i].Doom));
        }
        this.Touch = default;
    }

    public void BuildTicCmd(TicCmd cmd)
    {
        var keyForward = this.keys.IsDown(AppKey.W) || this.Touch.Forward;
        var keyBackward = this.keys.IsDown(AppKey.S) || this.Touch.Backward;
        var keyStrafeLeft = this.keys.IsDown(AppKey.A);
        var keyStrafeRight = this.keys.IsDown(AppKey.D);
        var keyTurnLeft = this.keys.IsDown(AppKey.Left) || this.Touch.TurnLeft;
        var keyTurnRight = this.keys.IsDown(AppKey.Right) || this.Touch.TurnRight;
        var keyFire = this.keys.IsDown(AppKey.Space) || this.keys.IsDown(AppKey.Ctrl) || this.Touch.Fire;
        var keyUse = this.keys.IsDown(AppKey.Shift) || this.keys.IsDown(AppKey.E) || this.Touch.Use;

        cmd.Clear();

        // No run key: the phone runs the marine flat out, which frees Shift to be Use.
        var speed = this.config.game_alwaysrun ? 1 : 0;

        var forward = 0;
        var side = 0;

        if (keyTurnLeft || keyTurnRight)
        {
            this.turnHeld++;
        }
        else
        {
            this.turnHeld = 0;
        }

        // A tap turns slowly so doorways are aimable; holding accelerates to the run-speed turn.
        var turnSpeed = this.turnHeld < PlayerBehavior.SlowTurnTics ? 2 : speed;

        if (keyTurnRight)
        {
            cmd.AngleTurn -= (short)PlayerBehavior.AngleTurn[turnSpeed];
        }
        if (keyTurnLeft)
        {
            cmd.AngleTurn += (short)PlayerBehavior.AngleTurn[turnSpeed];
        }

        // Drag-to-look, consumed as it is applied so one drag turns the player once.
        if (this.pendingTurn != 0f)
        {
            var turn = (int)MathF.Round(0.5f * this.config.mouse_sensitivity * this.pendingTurn) * 0x8;
            cmd.AngleTurn -= (short)Math.Clamp(turn, short.MinValue, short.MaxValue);
            this.pendingTurn = 0f;
        }

        if (keyForward)
        {
            forward += PlayerBehavior.ForwardMove[speed];
        }
        if (keyBackward)
        {
            forward -= PlayerBehavior.ForwardMove[speed];
        }
        if (keyStrafeLeft)
        {
            side -= PlayerBehavior.SideMove[speed];
        }
        if (keyStrafeRight)
        {
            side += PlayerBehavior.SideMove[speed];
        }

        if (keyFire)
        {
            cmd.Buttons |= TicCmdButtons.Attack;
        }
        if (keyUse)
        {
            cmd.Buttons |= TicCmdButtons.Use;
        }

        for (var i = 0; i < 7; i++)
        {
            if (this.keys.IsDown(AppKey.D1 + i))
            {
                cmd.Buttons |= TicCmdButtons.Change;
                cmd.Buttons |= (byte)(i << TicCmdButtons.WeaponShift);
                break;
            }
        }

        cmd.ForwardMove += (sbyte)Math.Clamp(forward, -PlayerBehavior.MaxMove, PlayerBehavior.MaxMove);
        cmd.SideMove += (sbyte)Math.Clamp(side, -PlayerBehavior.MaxMove, PlayerBehavior.MaxMove);
    }

    public void Reset()
    {
    }

    public void GrabMouse()
    {
    }

    public void ReleaseMouse()
    {
    }

    public int MaxMouseSensitivity => 15;

    public int MouseSensitivity
    {
        get => this.config.mouse_sensitivity;
        set => this.config.mouse_sensitivity = value;
    }

    /// <summary>What the on-screen pad is holding this frame.</summary>
    public struct TouchState
    {
        public bool Forward;
        public bool Backward;
        public bool TurnLeft;
        public bool TurnRight;
        public bool Fire;
        public bool Use;
    }
}
