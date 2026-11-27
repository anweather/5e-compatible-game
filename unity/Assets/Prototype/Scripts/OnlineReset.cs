// Resetting the world while playing together (J-014): the host decides. The host's Reset (R / View) summons the dragon
// for everyone (Summon, IPeers.SummonDragon), on the same tick on every machine; a client's asks the host, whose log
// says who asked. Holding Reset is the way out of a world that has gone wrong (Ask): the host stamps a rebuild for
// everyone (IPeers.StampReset) with a 3 s countdown, which every machine shows (ResetCountdown, in Net), and at its tick
// NetChem rebuilds the world; the view's own parts follow ChemGrid.WorldReset when Happened says it's a live reset:
// ThingSpawner brings every thing back with its id, NetWizard puts each player back at the start, NetCourt goes back to
// 0-0. Solo, Reset summons the dragon and holding it reloads the scene (EventLog).
public static class OnlineReset
{
    public static bool Summon()
    {
        var net = Peers.InPlay;
        if (!net.Online()) return false;
        if (net.Role == PeerRole.Host) net.SummonDragon();
        else AskHost();
        return true;
    }

    public static bool Ask()
    {
        var net = Peers.InPlay;
        if (!net.Online()) return false;
        if (net.Role == PeerRole.Host)
        {
            net.StampReset();
            return true;
        }
        AskHost();
        return true;
    }

    static void AskHost()
    {
        var me = NetWizard.Local;
        if (me != null) me.AskForReset();
        EventLog.Add("You asked the host to reset the world");
    }

    public static bool Happened(IPeers peers) => peers.Resetting;
}
