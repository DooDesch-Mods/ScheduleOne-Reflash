namespace Reflash.Wire
{
    /// <summary>
    /// What one replaced phone app needs from the game, and nothing else.
    ///
    /// Every app is two halves that meet here. The half under Apps/ owns the wire protocol - what a state call
    /// returns, what a command means, which errors exist - and references no engine assembly, so the headless suite
    /// compiles it in a second and catches an accidental dependency there instead of in a game launch. The half
    /// under Game/ implements this interface against the real managers.
    ///
    /// The three channels are the whole contract. A page reads with <c>&lt;app&gt;.state</c>, acts with
    /// <c>&lt;app&gt;.act</c>, and is told to read again by <c>&lt;app&gt;.changed</c>. Twenty-one handlers across seven
    /// apps rather than seventy: one name per purpose keeps the fake in the test suite small enough to be honest,
    /// and it is the shape that made a companion device cheap - a known, small read set can be prefetched, an open
    /// set of ad-hoc calls cannot.
    /// </summary>
    internal interface IAppPort
    {
        /// <summary>
        /// The app's id, which is also its Sideload app id and its bundle folder. "reflash-messages" and so on.
        /// </summary>
        string Id { get; }

        /// <summary>
        /// The vanilla app this one replaces, for the hijack to route an intercepted open to the right place.
        /// </summary>
        VanillaApp Replaces { get; }

        /// <summary>
        /// Everything the page needs to draw one section, as JSON. The argument selects the section - "" is the
        /// default view, "thread:npc_id" one conversation - so a page never pays for data it is not showing.
        ///
        /// Runs on the main thread inside the page's call. Must not emit: pushing an event from inside a handler
        /// re-enters the script engine that is currently running it.
        /// </summary>
        string State(string section);

        /// <summary>
        /// Do something. Returns <see cref="Reply.Ok"/> or one of the error codes; the page turns a code into words
        /// it owns. Every mutation goes through the game's own ServerRpc, so this never reimplements a rule the game
        /// already enforces - and never needs a host check, because those RPCs do not require ownership.
        /// </summary>
        string Act(Cmd cmd);

        /// <summary>
        /// A number that changes whenever anything this app shows has changed. The pulse compares it against what it
        /// last pushed and emits <c>&lt;app&gt;.changed</c> when they differ, so an idle app costs one integer
        /// comparison per tick rather than a serialisation.
        ///
        /// Cheap by contract. Anything expensive here is paid every tick for every app.
        /// </summary>
        int Revision { get; }

        /// <summary>
        /// What belongs on the app's icon badge, or zero for none. Read every tick alongside the revision.
        /// </summary>
        int Badge { get; }
    }

    /// <summary>
    /// For a source that has to publish a runtime picture - today only the map, with the extracted map image.
    ///
    /// A separate interface rather than a method on every source: exactly one of the seven needs this, and adding
    /// it to all of them would be six empty implementations pretending the capability is general.
    ///
    /// It exists at all because registering an app a SECOND time to reach Image() is not equivalent to using its
    /// handle - Sideload treats a repeat registration as a replacement, which silently dropped the iconless flag
    /// and the declared orientations, and the picture never reached the mounted page.
    /// </summary>
    internal interface INeedsAppHandle
    {
        void UseHandle(Sideload.Api.AppHandle handle);
    }

    /// <summary>
    /// For a port with one-off work that must not happen inside a page's call.
    ///
    /// The map has to extract the map picture, which is a full texture readback and a PNG encode - a good third of
    /// a second. Doing that on first use meant doing it inside <c>s1.call</c>, and Jint stops a handler after
    /// 250ms: the app did not come up slowly, it came up dead, with the script killed part-way through its first
    /// render.
    ///
    /// So the pulse calls this on every tick and the implementation is expected to do nothing after the first -
    /// which puts the cost in a frame of its own, before anyone opens the app.
    /// </summary>
    internal interface IWarmUp
    {
        void WarmUp();
    }

    /// <summary>
    /// The seven vanilla phone apps. Named rather than typed so the wire half can talk about them without
    /// referencing the game - the hijack maps each to its concrete IL2CPP type.
    /// </summary>
    internal enum VanillaApp
    {
        Messages,
        Map,
        Delivery,
        ProductManager,
        Contacts,
        DealerManagement,
        Journal,
    }
}
