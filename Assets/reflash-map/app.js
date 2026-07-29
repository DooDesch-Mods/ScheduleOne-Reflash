// Map. Vanilla's is a ScrollRect - a 2048x2048 Content dragged behind a 1205x617 window, wheel-zoomed about the
// cursor between scale 0.8 and 2.0. This is that, in a transform.
//
// Two things make it affordable. Writing a transform repaints one box instead of rebuilding the page, so a pan is
// not five hundred boxes of layout per frame. And the renderer reports a drag as deltaX/deltaY already measured in
// css pixels against the PAGE, not against the element - which matters here more than anywhere, because the element
// being measured is the one the drag is moving.
//
// On a real phone the same page does more: pinch, momentum, double-tap. All of it hangs off `s1.rich`, which only
// the companion's bridge sets - the renderer never takes any of those branches.

var WORLD = 1250.8;            // 2048 canvas units / 1.6375, matches .world in app.css
var ZOOM_MIN = 0.8;            // PinchableScrollRect.lowerScale
var ZOOM_MAX = 2.0;            // PinchableScrollRect.upperScale
var ZOOM_FOCUS = 1.3;          // MapApp.FocusPosition, the zoom the map opens at
var WHEEL_STEP = 0.22;         // one notch, which vanilla eases in over about half a second

// Whether this page is running in a real browser. `typeof` rather than `window.s1`, because the renderer has no
// `window` at all and touching it is not a falsy read - it is an error that kills the script before it has drawn
// anything.
var RICH = !!(typeof s1 !== 'undefined' && s1.rich);

var worldEl = document.getElementById('world');
var pinsEl = document.getElementById('pins');
var viewportEl = document.getElementById('viewport');
var nameEl = document.getElementById('poi-name');
var gripH = document.getElementById('grip-h');
var gripV = document.getElementById('grip-v');

var state = { pins: [], player: null, image: false, focus: '' };

// Where the map sits: the translate applied to #world, in css pixels, and the scale.
var tx = 0;
var ty = 0;
var zoom = ZOOM_FOCUS;
var placed = false;            // has the view been put somewhere yet

function read() {
  var raw = s1.call('reflash-map.state', '');
  if (!raw) return;

  try {
    state = JSON.parse(raw);
  } catch (e) {
    s1.log('state did not parse: ' + e);
    return;
  }

  if (!state.pins) state.pins = [];

  // Opened from somewhere else - the task list or a contact - with a place to look at.
  if (state.focus && state.focus.indexOf('at=') === 0) {
    var parts = state.focus.substring(3).split(',');
    var fx = parseFloat(parts[0]);
    var fy = parseFloat(parts[1]);

    if (!isNaN(fx) && !isNaN(fy)) {
      zoom = ZOOM_FOCUS;
      centreOn(fx, fy);
      placed = true;
    }
  }

  // Vanilla centres on the player the FIRST time the app opens in a session and keeps the view every time after -
  // so this happens once, when the page is built, and never again.
  if (!placed && state.player) {
    zoom = ZOOM_FOCUS;
    centreOn(state.player.x, state.player.y);
    placed = true;
  }
}

// The window the map is seen through, in css pixels. An element's geometry cannot be read back here, so it is
// derived from the screen's - minus the strip along the top, whose height app.css and this line have to agree on.
//
// The screen is 400 css px across and, in the game, 733.4 down. On a real device only the SHORT side is fixed:
// s1.screen reports what the page actually got, which on a 3:2 tablet is 400x640 rather than 400x733.4. Reading it
// is the difference between a map that fills the phone and one that stops short of the bottom.
function view() {
  var portrait = s1.orientation === 'portrait';
  var bar = portrait ? 30 : 24.4;

  if (typeof s1 !== 'undefined' && s1.screen && s1.screen.w > 0)
    return { w: s1.screen.w, h: s1.screen.h - bar };

  return portrait ? { w: 400, h: 733.4 - bar } : { w: 733.4, h: 400 - bar };
}

function centreOn(x, y) {
  var v = view();
  tx = v.w / 2 - x * WORLD * zoom;
  ty = v.h / 2 - y * WORLD * zoom;
}

function render() {
  renderPins();
  apply();
}

// Every marker on screen, in the order they were drawn. Kept because the markers are hit-tested by hand - see
// pickAt - so nothing else holds on to them.
var marks = [];

function renderPins() {
  pinsEl.replaceChildren();
  marks = [];
  picked = null;
  nameEl.className = 'poi-name';

  if (!state.image) {
    // No map picture could be extracted. A plain backdrop rather than nothing - the markers are still meaningful
    // relative to each other.
    var blank = document.createElement('div');
    blank.className = 'nomap';
    pinsEl.appendChild(blank);
  }

  for (var i = 0; i < state.pins.length; i++) marker(state.pins[i]);
  if (state.player) pinsEl.appendChild(player(state.player));
}

// Position written as ONE style attribute, not as two style properties.
//
// This is the difference between the map rendering and not. Every `el.style.left = ...` re-parses the element's
// whole inline declaration block and writes it back; doing that twice per marker, a hundred times over, blew
// through Jint's 250ms handler budget and the script was killed mid-render - so the app came up empty rather than
// slow. setAttribute writes the string once.
function place(el, x, y, w, h) {
  el.setAttribute('style', 'left:' + (x * WORLD - w / 2).toFixed(1) + 'px;top:'
                         + (y * WORLD - h / 2).toFixed(1) + 'px');
}

function marker(p) {
  // A potential customer or dealer is an AREA - vanilla draws the disc they can be found somewhere inside, and the
  // disc is the useful half of the marker.
  if (p.radius > 0) {
    var area = document.createElement('div');
    area.className = 'area' + (p.kind === 'dealer' ? ' dealer' : '');

    var d = p.radius * 2 * WORLD;
    area.setAttribute('style', 'left:' + (p.x * WORLD - d / 2).toFixed(1) + 'px;top:'
                             + (p.y * WORLD - d / 2).toFixed(1) + 'px;width:' + d.toFixed(1)
                             + 'px;height:' + d.toFixed(1) + 'px');
    pinsEl.appendChild(area);
  }

  var el;

  if (p.kind === 'customer' || p.kind === 'dealer' || p.kind === 'supplier') {
    // A ring with the mugshot masked into it.
    el = document.createElement('div');
    el.className = 'face';
    place(el, p.x, p.y, 18.3, 18.3);

    if (p.face) {
      var img = document.createElement('img');
      img.setAttribute('src', 's1://face-' + p.id);
      el.appendChild(img);
    }
  } else {
    // A round badge with a white glyph in it - a house for a property, a briefcase for a supplier's stash - which
    // is what the vanilla prefabs carry.
    el = document.createElement('div');
    el.className = 'pin' + (p.kind === 'stash' ? ' stash' : '');
    place(el, p.x, p.y, 12.2, 12.2);

    var glyph = document.createElement('img');
    glyph.setAttribute('src', p.kind === 'stash' ? 'glyph-stash.png' : 'glyph-home.png');
    el.appendChild(glyph);
  }

  // Remembered rather than given a click listener of its own - see pickAt for why.
  marks.push({ el: el, poi: p });

  pinsEl.appendChild(el);
}

function player(p) {
  var el = document.createElement('div');
  el.className = 'player';
  place(el, p.x, p.y, 48.9, 48.9);

  var dot = document.createElement('div');
  dot.className = 'dot';
  el.appendChild(dot);

  return el;
}

var picked = null;

// Which marker a tap landed on, worked out from the tap's position rather than by giving every marker a click
// handler of its own.
//
// That is not a shortcut, it is the only thing that works. A handler makes the renderer put a transparent hit quad
// over the element - and the viewport, which has to catch the drag, gets one too. The viewport's is a later sibling
// than the whole map inside it, so it is drawn on top and swallows every marker underneath. Hit-testing here also
// spares a hundred and twenty extra quads on a screen that is already the heaviest of the seven.
function pickAt(px, py) {
  var wx = (px - tx) / (WORLD * zoom);
  var wy = (py - ty) / (WORLD * zoom);

  // A finger is wider than a marker. The reach is measured on SCREEN and converted, so it stays a thumb's width at
  // every zoom rather than shrinking with the map.
  var reach = (RICH ? 22 : 12) / (WORLD * zoom);

  var best = null;
  var bestDistance = reach * reach;

  for (var i = 0; i < marks.length; i++) {
    var p = marks[i].poi;
    var dx = p.x - wx;
    var dy = p.y - wy;
    var d = dx * dx + dy * dy;

    if (d <= bestDistance) { bestDistance = d; best = marks[i]; }
  }

  return best;
}

// Vanilla names a marker while the pointer rests on it. A phone has no pointer to rest, so the name is what a tap
// gets - the same information by the only gesture a thumb has.
viewportEl.addEventListener('click', function (e) {
  if (typeof e.offsetX !== 'number') { clearPick(); return; }

  var hit = pickAt(e.offsetX, e.offsetY);
  if (!hit || !hit.poi.label) { clearPick(); return; }

  if (picked === hit.el) { clearPick(); return; }

  clearPick();
  picked = hit.el;
  hit.el.className = hit.el.className + ' picked';

  nameEl.textContent = hit.poi.label;
  nameEl.className = 'poi-name on';

  buzz(8);
});

function clearPick() {
  if (picked) picked.className = picked.className.replace(' picked', '');
  picked = null;
  nameEl.className = 'poi-name';
}

// ---- moving the map ------------------------------------------------------------------------------------------

// Keep the window inside the picture, which is exactly what vanilla's ScrollRect clamp does: the viewport rect may
// never leave the content rect.
function apply() {
  var v = view();

  if (zoom < ZOOM_MIN) zoom = ZOOM_MIN;
  if (zoom > ZOOM_MAX) zoom = ZOOM_MAX;

  var span = WORLD * zoom;

  tx = Math.min(0, Math.max(v.w - span, tx));
  ty = Math.min(0, Math.max(v.h - span, ty));

  worldEl.style.transform = 'translate(' + tx.toFixed(1) + 'px, ' + ty.toFixed(1) + 'px) scale('
                          + zoom.toFixed(3) + ')';

  // The two hairline scrollbars, which are the only chrome vanilla's map has.
  var wide = Math.max(6, v.w / span * (v.w - 18));
  var tall = Math.max(6, v.h / span * (v.h - 18));

  gripH.setAttribute('style', 'left:' + (-tx / span * (v.w - 18)).toFixed(1) + 'px;width:' + wide.toFixed(1) + 'px');
  gripV.setAttribute('style', 'top:' + (-ty / span * (v.h - 18)).toFixed(1) + 'px;height:' + tall.toFixed(1) + 'px');
}

// Zoom about a point of the WINDOW, so whatever is under the cursor or between two fingers stays put - the same
// thing PinchableScrollRect does by moving the content pivot onto the cursor.
function zoomAt(next, px, py) {
  if (next < ZOOM_MIN) next = ZOOM_MIN;
  if (next > ZOOM_MAX) next = ZOOM_MAX;
  if (next === zoom) return;

  var wx = (px - tx) / (WORLD * zoom);
  var wy = (py - ty) / (WORLD * zoom);

  zoom = next;
  tx = px - wx * WORLD * zoom;
  ty = py - wy * WORLD * zoom;

  apply();
}

// Drag to pan, one to one with the hand, which is what vanilla's ScrollRect does with the mouse and what every map
// on a phone does with a thumb.
var vx = 0;
var vy = 0;
var gliding = false;

viewportEl.addEventListener('dragstart', function () {
  vx = 0;
  vy = 0;
  gliding = false;
  busy(true);
});

viewportEl.addEventListener('drag', function (e) {
  if (pinching || (!e.deltaX && !e.deltaY)) return;

  tx += e.deltaX;
  ty += e.deltaY;

  // Kept for the glide. Blended rather than replaced so one stuttering frame does not decide the whole throw.
  vx = vx * 0.6 + e.deltaX * 0.4;
  vy = vy * 0.6 + e.deltaY * 0.4;

  apply();
});

viewportEl.addEventListener('dragend', function () {
  busy(false);
  glide();
});

// Momentum. Vanilla's ScrollRect has inertia too - it is not a phone affectation - so this runs in the game as well,
// off the script host's own timer since there is no animation frame to ask for.
function glide() {
  if (gliding) return;

  var speed = Math.abs(vx) + Math.abs(vy);
  if (speed < 0.6) return;

  // Scheduled rather than run here: calling it straight away adds a whole frame of travel in the same instant the
  // finger lifts, which reads as the map jumping out from under it.
  gliding = true;
  setTimeout(step, 16);

  function step() {
    if (!gliding) return;

    tx += vx;
    ty += vy;
    vx *= 0.92;
    vy *= 0.92;

    apply();

    if (Math.abs(vx) + Math.abs(vy) < 0.15) { gliding = false; busy(false); return; }
    setTimeout(step, 16);
  }
}

function stopGlide() { gliding = false; }

// One wheel notch, positive away from the reader, which is zooming out. Anchored at the cursor: e.offsetX/offsetY
// are where in the window the pointer was.
viewportEl.addEventListener('wheel', function (e) {
  stopGlide();

  var v = view();
  var px = typeof e.offsetX === 'number' ? e.offsetX : v.w / 2;
  var py = typeof e.offsetY === 'number' ? e.offsetY : v.h / 2;

  zoomAt(zoom + (e.wheelDelta > 0 ? -WHEEL_STEP : WHEEL_STEP), px, py);
});

// The phone can be turned, and the window is a different shape afterwards - so the transform has to be redone.
// Nothing else changes: the document, the markers and the script all survive a turn.
document.addEventListener('orientationchange', apply);

s1.on('reflash-map.changed', function () {
  read();
  render();
});

read();
render();

// ---- the phone -----------------------------------------------------------------------------------------------
//
// Everything below runs only in a browser. The renderer never sets s1.rich, so none of it is reachable in the game -
// these are gestures the game has no way to deliver, not overrides of ones it does.

var pinching = false;

function buzz(ms) {
  if (RICH && navigator.vibrate) { try { navigator.vibrate(ms); } catch (e) { /* refused */ } }
}

// The scrollbars fade in while the map moves and out when it settles, the way a touch scrollbar does. In the game
// they are simply always there, as vanilla's are.
var settle = null;

function busy(on) {
  if (!RICH) return;

  document.querySelector('.bar-h').className = 'bar-h' + (on ? ' busy' : '');
  document.querySelector('.bar-v').className = 'bar-v' + (on ? ' busy' : '');

  if (settle) { clearTimeout(settle); settle = null; }
  if (on) return;

  settle = setTimeout(function () {
    document.querySelector('.bar-h').className = 'bar-h';
    document.querySelector('.bar-v').className = 'bar-v';
  }, 600);
}

if (RICH) {
  var touches = {};
  var pinchFrom = 0;
  var pinchZoom = 1;
  var lastTap = 0;

  function spread() {
    var ids = Object.keys(touches);
    if (ids.length < 2) return null;

    var a = touches[ids[0]];
    var b = touches[ids[1]];

    return {
      d: Math.sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y)),
      x: (a.x + b.x) / 2,
      y: (a.y + b.y) / 2,
    };
  }

  // Where a page point lands inside the window, in the css pixels the layout is written in. The screen is scaled to
  // fill the device, so a client coordinate has to come back through that scale before it means anything here.
  function local(clientX, clientY) {
    var box = viewportEl.getBoundingClientRect();
    var k = box.width / viewportEl.offsetWidth;

    return { x: (clientX - box.left) / k, y: (clientY - box.top) / k };
  }

  viewportEl.addEventListener('touchstart', function (e) {
    for (var i = 0; i < e.changedTouches.length; i++) {
      var t = e.changedTouches[i];
      var p = local(t.clientX, t.clientY);
      touches[t.identifier] = p;
    }

    var s = spread();
    if (s) {
      // A second finger landed: the pan stops and the pinch takes over. Both at once fights itself.
      pinching = true;
      stopGlide();
      pinchFrom = s.d;
      pinchZoom = zoom;
      busy(true);
      return;
    }

    // Double tap to zoom in a step, and again past the top to come all the way back out - the gesture every map on
    // a phone has.
    var now = Date.now();
    if (now - lastTap < 280) {
      var at = local(e.changedTouches[0].clientX, e.changedTouches[0].clientY);
      zoomAt(zoom >= ZOOM_MAX - 0.01 ? ZOOM_MIN : zoom + 0.6, at.x, at.y);
      buzz(10);
      lastTap = 0;
      return;
    }

    lastTap = now;
  }, { passive: true });

  viewportEl.addEventListener('touchmove', function (e) {
    for (var i = 0; i < e.changedTouches.length; i++) {
      var t = e.changedTouches[i];
      touches[t.identifier] = local(t.clientX, t.clientY);
    }

    if (!pinching) return;

    var s = spread();
    if (!s || pinchFrom <= 0) return;

    e.preventDefault();
    zoomAt(pinchZoom * (s.d / pinchFrom), s.x, s.y);
  }, { passive: false });

  function lift(e) {
    for (var i = 0; i < e.changedTouches.length; i++) delete touches[e.changedTouches[i].identifier];

    if (Object.keys(touches).length >= 2) return;

    if (pinching) {
      // Every finger has to come off before panning is allowed again, or lifting one of two hands the map a jump
      // the size of the gap between them.
      if (Object.keys(touches).length === 0) { pinching = false; busy(false); }
      else pinchFrom = 0;
    }
  }

  viewportEl.addEventListener('touchend', lift, { passive: true });
  viewportEl.addEventListener('touchcancel', lift, { passive: true });
}
