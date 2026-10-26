using System;
using System.Collections;
using System.Linq;
using System.Reflection;

// Multiplayer Play Mode's virtual players (Window > Multiplayer > Multiplayer
// Play Mode), switched on and off as that window's checkboxes do. MPPM 2.0
// keeps that API internal (Unity.Multiplayer.PlayMode.Editor.MultiplayerPlaymode
// in UnityEditor.MultiplayerModule), so it is reached by reflection; if a
// Unity update moves it, these throw with what is missing. A virtual player is
// a second Editor process that MPPM owns: stop it here or in that window,
// never by killing it.
public static class VirtualPlayers
{
    const string TypeName = "Unity.Multiplayer.PlayMode.Editor.MultiplayerPlaymode";

    static object Player(string name)
    {
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(TypeName)).FirstOrDefault(t => t != null)
            ?? throw new InvalidOperationException($"Multiplayer Play Mode's {TypeName} wasn't found (package com.unity.multiplayer.playmode missing or changed)");
        var players = (IEnumerable)type.GetProperty("Players", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        foreach (var p in players)
            if ((string)p.GetType().GetProperty("Name").GetValue(p) == name) return p;
        throw new InvalidOperationException($"No Multiplayer Play Mode player called '{name}'");
    }

    /// <summary>NotLaunched, Launching, Launched or UnexpectedlyStopped.</summary>
    public static string State(string name)
    {
        var p = Player(name);
        return p.GetType().GetProperty("PlayerState").GetValue(p).ToString();
    }

    public static bool Running(string name) => State(name) == "Launched";

    /// <summary>Starts the virtual player (its window's checkbox); it takes 15 s or more to come up.</summary>
    public static void Activate(string name) => Call(name, "Activate");

    /// <summary>Stops the virtual player through MPPM.</summary>
    public static void Deactivate(string name) => Call(name, "Deactivate");

    static void Call(string name, string method)
    {
        var p = Player(name);
        var m = p.GetType().GetMethods().First(x => x.Name == method);
        var args = new object[m.GetParameters().Length];
        if (!(bool)m.Invoke(p, args)) throw new InvalidOperationException($"{method} {name} failed: {args[0]}");
    }
}
