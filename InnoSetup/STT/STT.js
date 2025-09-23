// stt.js  — MAIRA STT Bridge (AGC/EC/NS disabled; unity gain path)

// --- DOM refs ---
const statusElement = document.getElementById( 'status' );
const enableElement = document.getElementById( 'enable' );
const micSelectElement = document.getElementById( 'micSelect' );
const disconnectedElement = document.getElementById( 'disconnected' );
const waitingElement = document.getElementById( 'waiting' );
const listeningElement = document.getElementById( 'listening' );

// --- SpeechRecognition setup ---
const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;

let speechRecognition = null;
let webSocket = null;
let language = 'en-US';
let isPressed = false;

// --- Media state ---
let mediaStream = null;
let micDeviceId = localStorage.getItem( 'maira.micDeviceId' ) || '';

// --- Web Audio (for unity gain / future DSP) ---
let audioContext = null;
let sourceNode = null;
let unityGainNode = null;

// --- WS helpers ---
function post( type, data = {} )
{
    if ( webSocket && webSocket.readyState === WebSocket.OPEN )
    {
        webSocket.send( JSON.stringify( { type, ...data } ) );
    }
}
function wsUrl()
{
    const { protocol, host } = window.location;
    return ( protocol === 'https:' ? 'wss:' : 'ws:' ) + '//' + host + '/ws';
}

// --- UI strings ---
let strings = {
    title: "MAIRA STT Bridge",
    hint: "Choose an audio capture device, then click the enable button.",
    button: "Enable Speech-To-Text"
};

function applyStrings()
{
    const headTitleElement = document.getElementById( 'headTitle' );
    const bodyTitleElement = document.getElementById( 'bodyTitle' );
    const hintElement = document.getElementById( 'hint' );

    headTitleElement.textContent = strings.title;
    bodyTitleElement.textContent = strings.title;
    hintElement.textContent = strings.hint;
    enableElement.textContent = strings.button;
}

function setStatus( which )
{
    disconnectedElement.classList.add( 'hidden' );
    waitingElement.classList.add( 'hidden' );
    listeningElement.classList.add( 'hidden' );

    switch ( which )
    {
        case 0: disconnectedElement.classList.remove( 'hidden' ); break;
        case 1: waitingElement.classList.remove( 'hidden' ); break;
        case 2: listeningElement.classList.remove( 'hidden' ); break;
    }
}

// ---------------------------------------------------------------------------
//                       Mic constraints & audio helpers
// ---------------------------------------------------------------------------

function supportedAudioConstraints()
{
    try { return navigator.mediaDevices.getSupportedConstraints?.() ?? {}; }
    catch { return {}; }
}

// Build strict constraints to disable browser audio processing.
// Includes legacy goog* flags for older Chromium builds (ignored elsewhere).
function makeMicConstraints( deviceId )
{
    const base = {
        // Selection
        ...( deviceId ? { deviceId: { exact: deviceId } } : {} ),
        // Turn OFF automatic processing
        autoGainControl: false,
        echoCancellation: false,
        noiseSuppression: false,
        // Helpful, usually supported
        channelCount: 1,
        // Legacy best-effort fallbacks (Chromium)
        advanced: [ { googAutoGainControl: false, googAutoGainControl2: false, googEchoCancellation: false, googNoiseSuppression: false } ]
    };

    // Remove any unsupported standard keys to avoid OverconstrainedError noise
    const sup = supportedAudioConstraints();
    for ( const k of [ "autoGainControl", "echoCancellation", "noiseSuppression", "channelCount" ] )
    {
        if ( !sup[ k ] ) delete base[ k ];
    }

    return { audio: base, video: false };
}

async function logTrackSettings( stream )
{
    try
    {
        const track = stream?.getAudioTracks?.()[ 0 ];
        if ( !track ) return;
        const settings = track.getSettings?.() || {};
        console.info( "[MAIRA STT] Mic settings negotiated:", settings );
    } catch { /* noop */ }
}

// Keep a unity (1.0) gain in our app's audio path; this does not move OS sliders.
async function attachUnityGain( stream )
{
    try
    {
        if ( !stream ) return;
        const Ctx = window.AudioContext || window.webkitAudioContext;
        if ( !Ctx ) return;

        audioContext = audioContext || new Ctx();

        // Disconnect old nodes (if any)
        try { sourceNode?.disconnect(); } catch { }
        try { unityGainNode?.disconnect(); } catch { }

        const inputNode = audioContext.createMediaStreamSource( stream );
        const gainNode = audioContext.createGain();
        gainNode.gain.value = 1.0; // "100%" within our capture chain

        // Route to destination for monitoring; swap to a Processor/Worklet if needed later
        inputNode.connect( gainNode ).connect( audioContext.destination );

        sourceNode = inputNode;
        unityGainNode = gainNode;
    } catch { /* noop */ }
}

async function reassertNoAgcOnExistingTrack()
{
    try
    {
        const track = mediaStream?.getAudioTracks?.()[ 0 ];
        if ( !track?.applyConstraints ) return;
        // Re-apply standard (non-legacy) subset
        const { audio } = makeMicConstraints( micDeviceId );
        const { deviceId, advanced, ...standardOnly } = audio;
        await track.applyConstraints( standardOnly );
        await logTrackSettings( mediaStream );
    } catch { /* noop */ }
}

// ---------------------------------------------------------------------------

function ensureWs()
{
    if ( webSocket && ( webSocket.readyState === WebSocket.OPEN || webSocket.readyState === WebSocket.CONNECTING ) ) return;

    webSocket = new WebSocket( wsUrl() );

    webSocket.onopen = () =>
    {
        setStatus( 0 );
        refreshDevices();
    };

    webSocket.onclose = () =>
    {
        setStatus( 0 );
        setTimeout( ensureWs, 1000 );
    };

    webSocket.onmessage = ( ev ) =>
    {
        try
        {
            const msg = JSON.parse( ev.data );

            if ( msg.type === 'start' )
            {
                startRecognition();
            } else if ( msg.type === 'stop' )
            {
                stopRecognition();
            } else if ( msg.type === 'setlanguage' )
            {
                if ( msg.language ) applyLanguage( msg.language );
            } else if ( msg.type === 'setstrings' )
            {
                strings = { ...strings, ...msg.strings };
                applyStrings();
            } else if ( msg.type === 'shutdown' )
            {
                stopRecognition();
                stopMediaStream();
                setStatus( 0 );
                window.close();
            }
        } catch { /* ignore malformed */ }
    };
}

ensureWs();

async function refreshDevices()
{
    // Request minimal access so labels populate; do it with our strict constraints.
    try { await navigator.mediaDevices.getUserMedia( makeMicConstraints( micDeviceId ) ); }
    catch { /* user may deny; device labels may be blank */ }

    const devices = await navigator.mediaDevices.enumerateDevices();
    const mics = devices.filter( d => d.kind === 'audioinput' );

    micSelectElement.innerHTML = '';

    for ( const d of mics )
    {
        const opt = document.createElement( 'option' );
        opt.value = d.deviceId;
        opt.text = d.label || ( 'Microphone ' + ( micSelectElement.length + 1 ) );
        if ( micDeviceId && d.deviceId === micDeviceId ) opt.selected = true;
        micSelectElement.appendChild( opt );
    }

    if ( !micDeviceId && mics.length )
    {
        micDeviceId = mics[ 0 ].deviceId;
    }
}

navigator.mediaDevices.addEventListener( 'devicechange', refreshDevices );

async function switchMic( deviceId )
{
    micDeviceId = deviceId || '';
    localStorage.setItem( 'maira.micDeviceId', micDeviceId );

    stopMediaStream();
    enableElement.classList.remove( 'hidden' );

    try
    {
        const constraints = makeMicConstraints( micDeviceId );
        mediaStream = await navigator.mediaDevices.getUserMedia( constraints );
        await logTrackSettings( mediaStream );
        await attachUnityGain( mediaStream );
        await reassertNoAgcOnExistingTrack(); // belt-and-suspenders
        setStatus( 0 );
    } catch ( e )
    {
        console.warn( "[MAIRA STT] mic open failed:", e );
        setStatus( 0 );
    }
}

micSelectElement.addEventListener( 'change', () => switchMic( micSelectElement.value ) );

function stopMediaStream()
{
    if ( mediaStream )
    {
        for ( const t of mediaStream.getTracks() )
        {
            try { t.stop(); } catch { }
        }
        mediaStream = null;
    }
    try { sourceNode?.disconnect(); } catch { }
    try { unityGainNode?.disconnect(); } catch { }
    sourceNode = null;
    unityGainNode = null;
}

// --- SpeechRecognizer lifecycle ---
function createRecognizer()
{
    if ( !SpeechRecognition )
    {
        post( 'error', { message: 'SpeechRecognition not supported in this browser.' } );
        return null;
    }

    if ( speechRecognition )
    {
        try { speechRecognition.abort(); } catch { }
    }

    speechRecognition = new SpeechRecognition();
    speechRecognition.lang = language;
    speechRecognition.interimResults = true;
    speechRecognition.continuous = true;
    speechRecognition.maxAlternatives = 1;

    speechRecognition.onresult = ( e ) =>
    {
        let partial = '', final = '';
        for ( let i = e.resultIndex; i < e.results.length; i++ )
        {
            const r = e.results[ i ];
            if ( r.isFinal ) final += r[ 0 ].transcript;
            else partial += r[ 0 ].transcript;
        }
        if ( partial ) post( 'stt', { partial } );
        if ( final ) post( 'stt', { final } );
    };

    speechRecognition.onerror = ( ev ) => post( 'error', { message: ev.error || 'unknown' } );

    speechRecognition.onend = () =>
    {
        post( 'end' );
        if ( isPressed )
        {
            // Restart shortly to keep continuous behavior
            setTimeout( () => { try { speechRecognition.start(); } catch { } }, 60 );
        } else
        {
            setStatus( 1 );
        }
    };

    enableElement.classList.add( 'hidden' );
    return speechRecognition;
}

function startRecognition()
{
    isPressed = true;

    if ( !speechRecognition ) createRecognizer();

    // If we don't yet hold a stream, open one with strict constraints.
    if ( !mediaStream )
    {
        navigator.mediaDevices.getUserMedia( makeMicConstraints( micDeviceId ) )
            .then( async ( s ) =>
            {
                mediaStream = s;
                await logTrackSettings( mediaStream );
                await attachUnityGain( mediaStream );
                await reassertNoAgcOnExistingTrack();
            } )
            .catch( () => { /* allow SR to run anyway */ } );
    } else
    {
        // Reassert on existing track in case something flipped.
        reassertNoAgcOnExistingTrack();
    }

    try
    {
        speechRecognition.start();
        setStatus( 2 );
    } catch ( e )
    {
        if ( !String( e ).includes( 'start' ) )
        {
            post( 'error', { message: e.message || 'start() failed' } );
        }
    }
}

function stopRecognition()
{
    isPressed = false;
    try { speechRecognition && speechRecognition.stop(); } catch { }
    setStatus( 1 );
}

function applyLanguage( newLanguage )
{
    if ( !newLanguage || ( newLanguage === language ) ) return;
    language = newLanguage;

    const wasRunning = isPressed;

    try { speechRecognition && speechRecognition.abort(); } catch { }

    createRecognizer();

    if ( wasRunning )
    {
        try { speechRecognition.start(); } catch { }
    }
}

// --- Enable button flow ---
enableElement.addEventListener( 'click', async () =>
{
    try
    {
        await switchMic( micSelectElement.value );
        createRecognizer();
        // Prime permissions & event handlers
        try { speechRecognition.start(); } catch { }
        try { speechRecognition.stop(); } catch { }
        setStatus( 1 );
    } catch ( e )
    {
        setStatus( 0 );
    }
} );

document.addEventListener( 'DOMContentLoaded', refreshDevices );
