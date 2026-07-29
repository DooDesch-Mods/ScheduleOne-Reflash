// Contacts. The relationship graph, one region at a time.
//
// Vanilla draws circles at authored positions with lines between the people who know each other. All of that comes
// over the wire already in css pixels - the mod reads it off the live RelationCircle components - so this page
// places rather than lays out. That is deliberate: a layout computed here would be a different picture of the same
// data, and someone who knows where a face sits would have to learn it again.
//
// Two budgets shape everything below. Jint kills a handler after 250ms, and a box costs about half a millisecond
// to build, so a region has to fit in roughly two hundred boxes: 45 people at three boxes each plus ~58 lines is
// most of that, which is why a node is a ring, a face and a notch and nothing more.
//
// Positions are written as ONE style attribute per element. Setting .style.left and then .style.top re-parses the
// whole inline declaration block twice per box; over a hundred boxes that alone is the difference between the page
// rendering and being killed part-way through.

var SEP = String.fromCharCode(31);

var viewportEl = document.getElementById('viewport');
var worldEl = document.getElementById('world');
var edgesEl = document.getElementById('edges');
var nodesEl = document.getElementById('nodes');
var tabsEl = document.getElementById('tabs');
var detailEl = document.getElementById('detail');

var regions = [];
var region = '';
var graph = { nodes: [], edges: [], w: 0, h: 0 };
var detail = null;
var selectedId = '';

// Where the graph is centred, in its own css pixels, and how far it is scaled.
var centre = { x: 0, y: 0 };
var zoom = 1;
// Vanilla opens at 1:1 and lets you pan - its own screenshot has a scrollbar under the graph. Fitting the
// whole region in instead made every face too small to recognise, which is the one thing this screen is for.
var fitted = false;

function parse(raw, fallback) {
  if (!raw) return fallback;
  try { return JSON.parse(raw); } catch (e) { s1.log('state did not parse: ' + e); return fallback; }
}

function readRegions() {
  var data = parse(s1.call('reflash-contacts.state', ''), null);
  regions = data && data.regions ? data.regions : [];

  if (!region) {
    // The first unlocked region, which is where vanilla starts too.
    for (var i = 0; i < regions.length; i++) {
      if (!regions[i].unlocked) continue;
      region = regions[i].id;
      break;
    }
  }
}

function readGraph() {
  if (!region) { graph = { nodes: [], edges: [], w: 0, h: 0 }; return; }

  var data = parse(s1.call('reflash-contacts.state', 'region:' + region), null);
  graph = data && data.nodes ? data : { nodes: [], edges: [], w: 0, h: 0 };

  if (!graph.edges) graph.edges = [];

  // Nobody chosen yet, or the chosen one is not in this region any more.
  if (!findNode(selectedId)) selectedId = graph.nodes.length ? graph.nodes[0].id : '';
}

function readDetail() {
  if (!selectedId) { detail = null; return; }

  var data = parse(s1.call('reflash-contacts.state', 'contact:' + selectedId), null);
  detail = data && data.contact ? data : null;
}

function findNode(id) {
  for (var i = 0; i < graph.nodes.length; i++) if (graph.nodes[i].id === id) return graph.nodes[i];
  return null;
}

// ---- drawing -------------------------------------------------------------------------------------------------

function renderTabs() {
  tabsEl.replaceChildren();

  for (var i = 0; i < regions.length; i++) {
    var r = regions[i];

    var tab = document.createElement('div');
    tab.className = 'tab' + (r.id === region ? ' on' : '') + (r.unlocked ? '' : ' locked');
    tab.textContent = r.name;
    tab.setAttribute('data-id', r.id);
    tab.addEventListener('click', tabClicked);

    tabsEl.appendChild(tab);
  }
}

function tabClicked(e) {
  var id = e.currentTarget.getAttribute('data-id');
  if (id === region) return;

  region = id;
  selectedId = '';

  readGraph();
  readDetail();
  renderTabs();
  renderGraph();
  renderDetail();
  fit();
}

function renderGraph() {
  edgesEl.replaceChildren();
  nodesEl.replaceChildren();

  worldEl.setAttribute('style', 'width:' + Math.round(graph.w) + 'px;height:' + Math.round(graph.h) + 'px');


  for (var i = 0; i < graph.edges.length; i++) {
    var e = graph.edges[i];

    var line = document.createElement('div');
    line.className = 'edge';

    // The bar has to END on the two circles, so it is placed by where its CENTRE must land and the top-left is
    // worked back from there - see topLeftFor.
    var at = topLeftFor(e.x, e.y, EDGE_W, e.len, e.deg);

    line.setAttribute('style',
      'left:' + at.x.toFixed(1) + 'px;' +
      'top:' + at.y.toFixed(1) + 'px;' +
      'height:' + e.len.toFixed(1) + 'px;' +
      'transform:rotate(' + e.deg.toFixed(1) + 'deg)');

    edgesEl.appendChild(line);
  }

  for (var n = 0; n < graph.nodes.length; n++) nodesEl.appendChild(node(graph.nodes[n]));

  // renderGraph rewrites the world's whole style attribute, which drops the pan with it.
  apply();
}

var EDGE_W = 5.5;
var NODE_R = 30.5;          // half of the 61px circle
var NOTCH_R = 28.7;         // 47 canvas units from the centre, where vanilla hangs its notch
var NOTCH_W = 7.3;
var NOTCH_H = 11;

// Where to put a rotated box's TOP-LEFT so that its centre ends up at (cx, cy).
//
// This exists because the two renderers rotate about different points. Sideload places every box with a top-left
// pivot and rotates about it; a browser's transform-origin defaults to the centre. Rather than hope one of them
// changes, the position is computed for top-left rotation and app.css pins the browser to `transform-origin: 0 0`
// - then both draw the same picture.
function topLeftFor(cx, cy, w, h, deg) {
  var rad = deg * Math.PI / 180;
  var cos = Math.cos(rad);
  var sin = Math.sin(rad);

  return {
    x: cx - (w / 2 * cos - h / 2 * sin),
    y: cy - (w / 2 * sin + h / 2 * cos),
  };
}

function box(className) {
  var el = document.createElement('div');
  el.className = className;
  return el;
}

function node(p) {
  var el = document.createElement('div');

  el.className = 'node'
    + (p.supplier ? ' supplier' : '')
    + (p.hidden ? ' hidden' : '')
    + (p.unlocked ? '' : ' locked')
    + (p.id === selectedId ? ' selected' : '');

  // Vanilla tints the portrait's backing from #3c3c3c to #780f0f across a customer's dependence.
  var a = p.addiction || 0;
  var r = Math.round(60 + (120 - 60) * a);
  var g = Math.round(60 + (15 - 60) * a);
  var b = g;

  el.setAttribute('style',
    'left:' + (p.x - 30.5).toFixed(1) + 'px;' +
    'top:' + (p.y - 30.5).toFixed(1) + 'px;' +
    'background:rgb(' + r + ',' + g + ',' + b + ')');

  el.setAttribute('data-id', p.id);
  el.addEventListener('click', nodeClicked);


  // The face only exists once the mod has published it - they arrive a few per tick so the app can open at once
  // rather than after forty-five texture reads.
  if (p.face) {
    var img = document.createElement('img');
    img.className = 'face';
    // setAttribute, not .src - see the note in reflash-messages: assigning the property does nothing here.
    img.setAttribute('src', 's1://face-' + p.id);
    el.appendChild(img);

    // Vanilla does not hide the face of someone you have not met - it tints the same picture black. There is no
    // tint to apply here, so a black square goes over it, which comes out the same and works on both screens.
    if (p.hidden) el.appendChild(box('blackout'));
  }

  if (!p.unlocked) el.appendChild(box('padlock'));

  // The rim goes on last of the round parts so the scale lies over the picture, the way vanilla layers them.
  var rim = document.createElement('img');
  rim.className = 'rim';
  rim.setAttribute('src', 'ring.png');
  el.appendChild(rim);

  var notch = document.createElement('div');
  notch.className = 'notch';

  // Vanilla's own formula: +90 degrees at no relationship, -90 at the top of the scale. Vanilla rotates a pivot
  // centred on the circle and hangs the notch off it; here the notch's own place on that circle is worked out and
  // it is rotated to match.
  var deg = 90 - 180 * Math.max(0, Math.min(1, (p.rel || 0) / 5));
  var rad = deg * Math.PI / 180;

  var at = topLeftFor(NODE_R + NOTCH_R * Math.sin(rad),
                      NODE_R - NOTCH_R * Math.cos(rad),
                      NOTCH_W, NOTCH_H, deg);

  notch.setAttribute('style',
    'left:' + at.x.toFixed(1) + 'px;top:' + at.y.toFixed(1) + 'px;' +
    'transform:rotate(' + deg.toFixed(1) + 'deg)');

  el.appendChild(notch);
  return el;
}

function nodeClicked(e) {
  var id = e.currentTarget.getAttribute('data-id');
  if (id === selectedId) return;

  selectedId = id;

  readDetail();
  renderGraph();
  renderDetail();
}

// ---- the detail panel ----------------------------------------------------------------------------------------

function renderDetail() {
  detailEl.replaceChildren();

  if (!detail) {
    detailEl.appendChild(text('d-empty', 'Select a contact'));
    return;
  }

  var c = detail.contact;

  detailEl.appendChild(text('d-name', c.name));
  detailEl.appendChild(text('d-type', kindWord(c.kind)));

  // Asked of the contact itself, not looked up in the graph. The graph holds one entry per relation circle and the
  // game gives some people two, so the lookup could land on the wrong copy - which is how a customer the player
  // knows perfectly well ended up with no relationship shown at all.
  var known = !!c.unlocked;

  if (known) {
    // c.rel is ALREADY 0..1 - the contact view normalises it, unlike a graph node, which carries the raw 0..5.
    // Dividing again put every relationship at a fifth of where it stands.
    scaleBand('Relationship', c.rel || 0, c.relLabel, relColour(c.rel || 0));
    rampBand('Addiction', (detail.addiction || 0) / 100, detail.addiction + '%');
  }

  if (detail.standards) {
    var block = document.createElement('div');
    block.className = 'd-block';
    block.appendChild(text('d-head', 'Standards'));

    var row = document.createElement('div');
    row.className = 'd-star';

    // One PNG per quality colour rather than one star tinted five ways: the renderer cannot tint an image, and a
    // white star beside a green word is not what vanilla shows.
    var star = document.createElement('img');
    star.setAttribute('src', 'star-' + (detail.standardsColour || '#ffffff').substring(1) + '.png');
    row.appendChild(star);

    var value = text('d-line', detail.standards);
    value.setAttribute('style', 'color:' + (detail.standardsColour || '#ffffff'));
    row.appendChild(value);

    block.appendChild(row);
    detailEl.appendChild(block);
  }

  lines('Favourite Effects', detail.properties, 'None');
  if (known) lines('Most Purchased Product (Weekly)', detail.purchases, 'No recent purchases');

  if (known) {
    var spent = document.createElement('div');
    spent.className = 'd-block';
    spent.appendChild(text('d-head', 'Total Spent (Weekly)'));
    spent.appendChild(text('d-line d-cash', '$' + (detail.spent || 0)));
    detailEl.appendChild(spent);
  }

  if (detail.debt) {
    var debt = document.createElement('div');
    debt.className = 'd-block';
    debt.appendChild(text('d-head', 'Debt'));
    debt.appendChild(text('d-line d-cash', '$' + detail.debt));
    detailEl.appendChild(debt);
  }

  if (detail.poi) {
    var button = text('d-map', 'Show on map');
    button.addEventListener('click', showOnMap);
    detailEl.appendChild(button);
  }
}

// The five relationship bands, as the game declares them in RelationshipCategory. A category is a THRESHOLD on the
// raw 0..5 delta - Loyal at 4, Friendly at 3, Neutral at 2, Unfriendly at 1 - and the same five colours make up the
// bar the marker slides along.
var BANDS = ['#ad3f3f', '#e38837', '#d0d0d0', '#3db5f3', '#3fd33f'];

function relColour(fill) {
  return BANDS[Math.max(0, Math.min(4, Math.floor(fill * 5 - 0.0001)))];
}

// Relationship: five blocks with a marker on them, not a bar that fills. Vanilla draws it as a Scrollbar over a
// five-colour strip, which is the same thing said in uGUI.
function scaleBand(title, fill, value, colour) {
  var block = document.createElement('div');
  block.className = 'd-block';
  block.appendChild(text('d-head', title));

  var track = document.createElement('div');
  track.className = 'd-band';

  for (var i = 0; i < BANDS.length; i++) {
    var seg = document.createElement('div');
    seg.className = 'seg';
    seg.setAttribute('style', 'background:' + BANDS[i]);
    track.appendChild(seg);
  }

  var mark = document.createElement('div');
  mark.className = 'd-mark';
  mark.setAttribute('style', 'left:' + (Math.max(0, Math.min(1, fill)) * 100).toFixed(1) + '%');
  track.appendChild(mark);

  block.appendChild(track);

  var label = text('d-value', value);
  label.setAttribute('style', 'color:' + colour);
  block.appendChild(label);

  detailEl.appendChild(block);
}

// Addiction: one strip running pale to red with a marker on it, and the number written in the colour it has
// reached - vanilla lerps the label between the same two ends.
function rampBand(title, fill, value) {
  var block = document.createElement('div');
  block.className = 'd-block';
  block.appendChild(text('d-head', title));

  var track = document.createElement('div');
  track.className = 'd-band d-ramp';

  var mark = document.createElement('div');
  mark.className = 'd-mark';
  mark.setAttribute('style', 'left:' + (Math.max(0, Math.min(1, fill)) * 100).toFixed(1) + '%');
  track.appendChild(mark);

  block.appendChild(track);

  var label = text('d-value', value);
  label.setAttribute('style', 'color:' + mix('#f0d5d5', '#e04a4a', Math.max(0, Math.min(1, fill))));
  block.appendChild(label);

  detailEl.appendChild(block);
}

// Two hex colours blended, because the renderer has no colour functions and the label's colour is a position on a
// ramp rather than one of a set.
function mix(from, to, t) {
  var out = '#';

  for (var i = 1; i < 7; i += 2) {
    var a = parseInt(from.substr(i, 2), 16);
    var b = parseInt(to.substr(i, 2), 16);
    var v = Math.round(a + (b - a) * t).toString(16);
    out += v.length < 2 ? '0' + v : v;
  }

  return out;
}

// Each line in its own colour where the game gives one - an effect is written in that effect's LabelColor, and the
// same effect reads the same in the product screen. Values are {text, colour} or plain strings.
function lines(title, values, empty) {
  var block = document.createElement('div');
  block.className = 'd-block';
  block.appendChild(text('d-head', title));

  if (!values || !values.length) {
    block.appendChild(text('d-line d-dim', empty));
  } else {
    for (var i = 0; i < values.length; i++) {
      var v = values[i];

      if (typeof v === 'string') {
        block.appendChild(text('d-line', v));
        continue;
      }

      // A middle dot, which the font atlases DO carry - vanilla's bullet at U+2022 would draw as an empty box.
      var line = text('d-line', '· ' + v.text);
      if (v.colour) line.setAttribute('style', 'color:' + v.colour);
      block.appendChild(line);
    }
  }

  detailEl.appendChild(block);
}

function kindWord(kind) {
  if (kind === 'dealer') return 'Dealer';
  if (kind === 'supplier') return 'Supplier';
  if (kind === 'customer') return 'Customer';
  return 'Contact';
}

function showOnMap() {
  if (!selectedId) return;
  s1.call('reflash-contacts.act', 'map' + SEP + selectedId);
}

function text(className, value) {
  var el = document.createElement('div');
  el.className = className;
  el.textContent = value;
  return el;
}

// ---- panning -------------------------------------------------------------------------------------------------

// Drag to move the graph around, the same gesture vanilla's scrollable board takes. The delta arrives in css
// pixels of the SCREEN, so it is divided by the zoom to become a distance across the board.
viewportEl.addEventListener('drag', function (e) {
  if (!e.deltaX && !e.deltaY) return;

  // Dragging is close work, so it leaves the fitted overview - staying fitted would snap the middle straight back.
  fitted = false;
  setZoomButtons();

  centre = {
    x: centre.x - e.deltaX / zoom,
    y: centre.y - e.deltaY / zoom,
  };

  apply();
});

document.getElementById('z-out').addEventListener('click', fit);
document.getElementById('z-in').addEventListener('click', close);

function centreOnSelection() {
  var node = findNode(selectedId);
  centre = node ? { x: node.x, y: node.y } : { x: graph.w / 2, y: graph.h / 2 };
  apply();
}

// The whole region at once. Vanilla opens zoomed out and lets you pinch in; this renderer has no pinch, so the
// two states are a button - and the fitted one is the default because a region is 2000px wide and the window is
// 521, and landing on one circle with no context is not the app.
function fit() {
  fitted = true;
  centre = { x: graph.w / 2, y: graph.h / 2 };
  setZoomButtons();
  apply();
}

function close() {
  fitted = false;
  setZoomButtons();
  centreOnSelection();
}

function setZoomButtons() {
  var out = document.getElementById('z-out');
  var into = document.getElementById('z-in');
  if (out) out.className = 'cbtn' + (fitted ? ' on' : '');
  if (into) into.className = 'cbtn' + (fitted ? '' : ' on');
}

// The graph's window, in css pixels: the screen minus the tab strip, and minus the detail panel - which sits beside
// the graph on its side and under it upright.
//
// The screen is 400 css px across and, in the game, 733.4 down. On a real device only the SHORT side is fixed, so
// s1.screen is asked first: a 3:2 tablet hands this page 400x640, and laying it out for 733.4 pushed the whole
// board off the bottom of the screen.
function viewBox(portrait) {
  var w = portrait ? 400 : 733.4;
  var h = portrait ? 733.4 : 400;

  if (typeof s1 !== 'undefined' && s1.screen && s1.screen.w > 0) { w = s1.screen.w; h = s1.screen.h; }

  return portrait
    ? { w: w, h: h - 250 - 30 }
    : { w: w - 214.4, h: h - 30 };
}

// The viewport's size, derived rather than measured - an element's geometry cannot be read back here. The detail
// panel and the tab strip take known slices out of it; those numbers and app.css are the same layout stated twice,
// which is the price of not being able to ask.
function apply() {
  var portrait = s1.orientation === 'portrait';
  var box = viewBox(portrait);
  var vw = box.w;
  var vh = box.h;

  zoom = fitted && graph.w > 0 && graph.h > 0
    ? Math.min(1, Math.min(vw / graph.w, vh / graph.h))
    : 1;

  // While fitted, the middle IS the middle of the graph - derived here rather than remembered, because the first
  // render can happen before the graph has arrived (the companion answers a read a moment late) and a centre
  // worked out from an empty graph would stick.
  if (fitted) centre = { x: graph.w / 2, y: graph.h / 2 };

  // The world is scaled about its top-left, so the offset is worked out in scaled pixels.
  var x = vw / 2 - centre.x * zoom;
  var y = vh / 2 - centre.y * zoom;

  worldEl.style.transform = 'translate(' + Math.round(x) + 'px, ' + Math.round(y) + 'px) scale(' + zoom.toFixed(3) + ')';
}

document.addEventListener('orientationchange', apply);

s1.on('reflash-contacts.changed', function () {
  readRegions();
  readGraph();
  readDetail();
  renderTabs();
  renderGraph();
  renderDetail();
});

readRegions();
readGraph();
readDetail();
renderTabs();
renderGraph();
renderDetail();
fit();
