// Messages, built from Workspace/docs/Reflash/vanilla-shots.
//
// Two screens, not two panes: a list you push a conversation over, and a back arrow that pops it. That is what the
// phone does, and holding both on screen at once was one of the ways the first attempt stopped looking like the
// game.
//
// On top of the thread sit two windows the GAME opens rather than this page: the counter-offer and a supplier's
// order sheet. Choosing "[Counter-offer]" or sending "I need to order a dead drop" runs game code that opens the
// real window and keeps the callback that completes the deal - so what is drawn here is a stand-in, and every
// change is applied to the real one when the player confirms.

var SEP = String.fromCharCode(31);   // U+001F, the command field separator

var appEl = document.getElementById('app');
var rowsEl = document.getElementById('rows');
var bubblesEl = document.getElementById('bubbles');
var repliesEl = document.getElementById('replies');
var sheetListEl = document.getElementById('sheet-list');

// The relationship band, stated once. app.css draws it and app.js places the marker on it, and the two drifting
// apart is exactly how the marker ended up sitting past the end of its own band.
var BAND_W = 123;   // the bar inside the band's 2px frame
var MARKER_W = 7;

var PICKER_PAGE = 25;   // as many per page as the vanilla picker shows

var list = { rev: 0, unread: 0, threads: [] };
var open = null;
var openId = '';

// Whether this conversation has ever come back from the mod. Distinguishes "not loaded yet", which a second
// screen goes through on every open, from "no longer exists", which is the only reason to leave the screen.
var seenThread = false;

// What the player has typed into the counter-offer so far. Seeded from the game the first time the window
// appears and left alone afterwards - a refresh mid-edit would otherwise put the numbers back.
var draft = null;
var picker = { open: false, term: '', page: 0 };

// The same for the order sheet: item id -> amount.
var cart = null;

function parse(raw, fallback) {
  if (!raw) return fallback;
  try { return JSON.parse(raw); } catch (e) { s1.log('state did not parse: ' + e); return fallback; }
}

function readList() {
  list = parse(s1.call('reflash-messages.state', ''), { rev: 0, unread: 0, threads: [] });
  if (!list.threads) list.threads = [];
}

var sheets = { counter: null, order: null, deal: null };

function readThread() {
  if (!openId) { open = null; sheets = { counter: null, order: null, deal: null }; return; }

  var data = parse(s1.call('reflash-messages.state', 'thread:' + openId), null);
  open = data && data.thread ? data.thread : null;

  sheets.counter = data ? (data.counter || null) : null;
  sheets.order = data ? (data.order || null) : null;
  sheets.deal = data ? (data.deal || null) : null;
}

// ---- the list --------------------------------------------------------------------------------------------------

function renderList() {
  rowsEl.replaceChildren();

  if (!list.threads.length) {
    rowsEl.appendChild(text('list-empty', 'No messages.'));
    return;
  }

  for (var i = 0; i < list.threads.length; i++) rowsEl.appendChild(row(list.threads[i]));
}

function row(t) {
  var el = document.createElement('div');
  el.className = 'row';
  el.setAttribute('data-id', t.id);
  el.addEventListener('click', rowClicked);

  el.appendChild(div('row-line'));

  // Only an unread row carries the dot; vanilla leaves the space empty rather than dimming it.
  if (t.unread) el.appendChild(div('row-dot'));

  var face = div('row-face');
  if (t.face) {
    face.appendChild(picture('s1://face-' + t.id));
  } else {
    face.textContent = t.known && t.name ? t.name.charAt(0) : '?';
  }
  el.appendChild(face);

  var name = div('row-name');
  name.appendChild(text('row-name-text', t.name));

  var badge = categoryOf(t);
  if (badge) name.appendChild(badge);
  el.appendChild(name);

  var preview = text('row-preview', '');
  rich(preview, t.preview);
  el.appendChild(preview);

  // The cross belongs to a customer's row and nowhere else. Drawn as a PNG in the bundle rather than typed as a
  // character: the game's text atlases carry Latin-1 only, so every dingbat comes out as an empty box, and an 'x'
  // in a font is visibly not the icon vanilla uses.
  if (t.canHide) {
    var hide = document.createElement('button');
    hide.className = 'row-hide';
    hide.setAttribute('data-id', t.id);
    hide.appendChild(picture('icon-x.png'));
    hide.addEventListener('click', hideClicked);
    el.appendChild(hide);
  }

  // How long the offer still stands, as a hairline along the bottom, red as it runs out.
  if (typeof t.offer === 'number' && t.offer >= 0) {
    var bar = div('row-offer' + (t.offer < 0.15 ? ' gone' : t.offer < 0.45 ? ' low' : ''));
    bar.setAttribute('style', 'width:' + Math.round(t.offer * 100) + '%');
    el.appendChild(bar);
  }

  return el;
}

// The letter vanilla puts after the name: C for a customer, S for a supplier, D for a dealer.
function categoryOf(t) {
  if (!t.cats || !t.cats.length) return null;

  var kind = String(t.cats[0]).toLowerCase();
  var known = kind === 'customer' || kind === 'supplier' || kind === 'dealer';

  return text('cat ' + (known ? kind : 'dealer'), kind.charAt(0).toUpperCase());
}

function rowClicked(e) {
  openId = e.currentTarget.getAttribute('data-id');
  seenThread = false;

  readThread();
  act('read' + SEP + openId);

  show();
  renderThread();
}

function hideClicked(e) {
  // The cross hides the row and must not also open the conversation under it.
  if (e.stopPropagation) e.stopPropagation();

  act('hide' + SEP + e.currentTarget.getAttribute('data-id') + SEP + '1');
}

// ---- one conversation ------------------------------------------------------------------------------------------

function renderThread() {
  if (!open) return;

  document.getElementById('head-name').textContent = open.name;

  var face = document.getElementById('head-face');
  face.replaceChildren();

  if (open.face) {
    face.appendChild(picture('s1://face-' + open.id));
  } else {
    face.textContent = open.known && open.name ? open.name.charAt(0) : '?';
  }

  // The relationship band, with the marker where this contact stands on it.
  var known = typeof open.rel === 'number' && open.rel >= 0;
  document.getElementById('head-band').setAttribute('style', known ? '' : 'display:none');
  if (known) {
    // Across the BAND's width less the marker's own, so 0 sits on the left edge and 1 on the right. The two
    // numbers have to match app.css; a stale 117 here against a 95px band put the marker a quarter too far right.
    document.getElementById('head-marker')
      .setAttribute('style', 'left:' + Math.round(open.rel * (BAND_W - MARKER_W)) + 'px');
  }

  // A customer shows the standards star, a supplier their debt. Never both.
  var isSupplier = typeof open.debt === 'number' && open.debt >= 0;
  var showStar = !!open.standardsColour && !isSupplier;

  var star = document.getElementById('head-star');
  star.setAttribute('style', showStar ? 'background:' + open.standardsColour : 'display:none');

  document.getElementById('head-debt').setAttribute('style', isSupplier ? '' : 'display:none');
  if (isSupplier) document.getElementById('head-debt-value').textContent = '$' + open.debt;

  renderBubbles();
  renderReplies();
}

function renderBubbles() {
  bubblesEl.replaceChildren();

  var messages = open.messages || [];
  for (var i = 0; i < messages.length; i++) {
    var m = messages[i];

    var bubble = document.createElement('div');
    bubble.className = 'bubble ' + (m.from === 'me' ? 'me' : 'them');
    bubble.style.alignSelf = m.from === 'me' ? 'flex-end' : 'flex-start';
    rich(bubble, m.text);
    bubble.appendChild(div('tail'));

    bubblesEl.appendChild(bubble);
  }

  bubblesEl.scrollToEnd();
}

function renderReplies() {
  repliesEl.replaceChildren();

  var replies = open.replies || [];
  for (var i = 0; i < replies.length; i++) {
    var button = text('reply', replies[i]);
    button.setAttribute('data-i', String(i));
    button.addEventListener('click', replyClicked);
    repliesEl.appendChild(button);
  }
}

function replyClicked(e) {
  // The token belongs to the ANSWERS, not to the app - see ReplyToken on the mod's side.
  act('reply' + SEP + openId + SEP + e.currentTarget.getAttribute('data-i') + SEP + (open ? open.replyToken : 0));
}

// ---- the send-message sheet ------------------------------------------------------------------------------------

function hasCompose() {
  return !!(open && open.sendables && open.sendables.length);
}

function inSheet() {
  return appEl.className.indexOf('on-sheet') >= 0;
}

/// The class list IS the screen state - which screen is showing, whether the compose button belongs, and which of
/// the game's two windows is up.
function show(sheet) {
  appEl.className = 'app'
    + (openId ? ' on-thread' : '')
    + (sheet ? ' on-sheet' : '')
    + (hasCompose() ? ' has-compose' : '')
    + (sheets.counter ? ' on-counter' : '')
    + (sheets.order ? ' on-order' : '')
    + (sheets.deal ? ' on-deal' : '')
    + (picker.open ? ' on-picker' : '');
}

document.getElementById('compose').addEventListener('click', function () {
  renderSheet();
  show(true);
});

document.getElementById('sheet-cancel').addEventListener('click', function () { show(false); });

function renderSheet() {
  sheetListEl.replaceChildren();

  var sendables = (open && open.sendables) || [];
  for (var i = 0; i < sendables.length; i++) {
    var s = sendables[i];

    // Vanilla greys a line out rather than hiding it, and the reason travels with it - "you have no debt to pay"
    // is more use than the line simply being gone.
    var button = text('sheet-item' + (s.valid ? '' : ' off'), s.valid ? s.text : s.text + ' - ' + s.reason);
    button.setAttribute('data-i', String(i));
    if (s.valid) button.addEventListener('click', sendClicked);

    sheetListEl.appendChild(button);
  }
}

function sendClicked(e) {
  act('send' + SEP + openId + SEP + e.currentTarget.getAttribute('data-i') + SEP + (open ? open.sendToken : 0));
  show(false);
}

// ---- the counter-offer -----------------------------------------------------------------------------------------

function renderCounter() {
  var c = sheets.counter;
  if (!c) { draft = null; picker.open = false; return; }

  // Seeded once. Re-seeding on every refresh would undo an edit the moment anything else in the app moved.
  if (!draft) draft = { id: c.productId, name: c.productName, quantity: c.quantity, price: c.price };

  var product = find(c.products, draft.id);
  var name = product ? product.name : draft.name;

  document.getElementById('co-name').textContent = draft.quantity + 'x ' + name;

  var icon = document.getElementById('co-icon');
  if (product && product.icon) {
    icon.setAttribute('src', 's1://item-' + draft.id);
    icon.setAttribute('style', '');
  } else {
    icon.setAttribute('style', 'display:none');
  }

  document.getElementById('co-price').textContent = '$' + draft.price;

  // The fair price follows the amount, so it is recomputed here rather than taken from what the game said when
  // the window opened.
  var unit = c.quantity > 0 ? c.fair / c.quantity : c.fair;
  document.getElementById('co-fair').textContent = 'Fair price: $' + Math.round(unit * draft.quantity);

  renderPicker(c);
}

function renderPicker(c) {
  var grid = document.getElementById('picker-grid');
  grid.replaceChildren();

  var all = c.products || [];
  var term = picker.term.toLowerCase();
  var hits = [];

  for (var i = 0; i < all.length; i++)
    if (!term || all[i].name.toLowerCase().indexOf(term) >= 0) hits.push(all[i]);

  var pages = Math.max(1, Math.ceil(hits.length / PICKER_PAGE));
  if (picker.page >= pages) picker.page = pages - 1;

  var from = picker.page * PICKER_PAGE;
  for (var k = from; k < Math.min(from + PICKER_PAGE, hits.length); k++) {
    var p = hits[k];

    var cell = document.createElement('button');
    cell.className = 'pick';
    cell.setAttribute('data-id', p.id);
    cell.setAttribute('title', p.name);

    if (p.icon) cell.appendChild(picture('s1://item-' + p.id));
    else cell.appendChild(text('letter', p.name.charAt(0)));

    cell.addEventListener('click', pickClicked);
    grid.appendChild(cell);
  }

  document.getElementById('page-label').textContent = (picker.page + 1) + ' / ' + pages;
  document.getElementById('picker-search').textContent = picker.term || 'Search products...';
}

function pickClicked(e) {
  if (e.stopPropagation) e.stopPropagation();

  draft.id = e.currentTarget.getAttribute('data-id');
  picker.open = false;

  show(inSheet());
  renderCounter();
}

document.getElementById('co-product').addEventListener('click', function () {
  picker.open = !picker.open;
  show(inSheet());
  renderCounter();
});

document.getElementById('page-prev').addEventListener('click', function (e) {
  if (e.stopPropagation) e.stopPropagation();
  if (picker.page > 0) { picker.page--; renderCounter(); }
});

document.getElementById('page-next').addEventListener('click', function (e) {
  if (e.stopPropagation) e.stopPropagation();
  picker.page++;
  renderCounter();
});

document.getElementById('qty-down').addEventListener('click', function () { changeQuantity(-1); });
document.getElementById('qty-up').addEventListener('click', function () { changeQuantity(1); });

function changeQuantity(by) {
  if (!draft || !sheets.counter) return;

  draft.quantity = clamp(draft.quantity + by, 1, sheets.counter.maxQuantity || 50);
  renderCounter();
}

// One listener on the row rather than seven: the amount is on the button that was pressed.
document.getElementById('co-money').addEventListener('click', function (e) {
  var by = e.target && e.target.getAttribute ? e.target.getAttribute('data-d') : null;
  if (!by || !draft) return;

  draft.price = clamp(draft.price + parseInt(by, 10), 1, 9999);
  renderCounter();
});

document.getElementById('co-send').addEventListener('click', function () {
  if (!draft) return;

  act('counter' + SEP + draft.id + SEP + draft.quantity + SEP + draft.price);
  draft = null;
  picker.open = false;
});

document.getElementById('counter-close').addEventListener('click', closeSheet);

// ---- a supplier's order sheet ----------------------------------------------------------------------------------

function renderOrder() {
  var o = sheets.order;
  if (!o) { cart = null; return; }

  if (!cart) cart = {};

  document.getElementById('order-title').textContent = o.title;
  document.getElementById('order-sub').textContent = o.subtitle;

  var panel = document.getElementById('order-panel');
  panel.replaceChildren();

  var total = 0;
  var count = 0;

  for (var i = 0; i < o.items.length; i++) {
    var item = o.items[i];
    var amount = cart[item.id] || 0;

    if (!item.locked) { total += item.price * amount; count += amount; }
    panel.appendChild(orderRow(item, amount));
  }

  var limit = o.limit || 0;
  var overSpend = total > limit;
  var overCount = count > (o.itemLimit || 10);

  document.getElementById('sum-total').className = 'v' + (overSpend ? ' over' : '');
  document.getElementById('sum-total').textContent = '$' + total;
  document.getElementById('sum-limit').textContent = '$' + limit;
  document.getElementById('sum-debt').textContent = '$' + (o.debt || 0);

  var items = document.getElementById('sum-items');
  items.className = 'v plain' + (overCount ? ' over' : '');
  items.textContent = count + '/' + (o.itemLimit || 10);

  var ready = count > 0 && !overSpend && !overCount;
  document.getElementById('order-send').className = 'order-send' + (ready ? '' : ' off');
}

function orderRow(item, amount) {
  var el = div('order-row');

  if (item.icon) el.appendChild(picture('s1://item-' + item.id));
  else el.appendChild(div('order-icon'));

  var mid = div('order-text');
  mid.appendChild(text('order-name', item.name));
  mid.appendChild(text('order-price', '$' + item.price));
  el.appendChild(mid);

  var qty = div('order-qty');
  qty.appendChild(stepper(item.id, -1));
  qty.appendChild(text('n', String(amount)));
  qty.appendChild(stepper(item.id, 1));
  el.appendChild(qty);

  // Vanilla covers a row the player has not ranked up to rather than hiding it, so the reason is visible.
  if (item.locked) {
    var lock = div('order-lock');
    lock.appendChild(picture('icon-lock.png'));
    lock.appendChild(text('why', item.lockText));
    el.appendChild(lock);
  }

  return el;
}

function stepper(id, by) {
  var button = text('step', by < 0 ? '-' : '+');
  button.setAttribute('data-id', id);
  button.setAttribute('data-d', String(by));
  button.addEventListener('click', orderStepped);
  return button;
}

function orderStepped(e) {
  var id = e.currentTarget.getAttribute('data-id');
  var by = parseInt(e.currentTarget.getAttribute('data-d'), 10);

  cart[id] = clamp((cart[id] || 0) + by, 0, 99);
  renderOrder();
}

document.getElementById('order-send').addEventListener('click', function () {
  if (!cart) return;

  var command = 'order';
  for (var id in cart) if (cart[id] > 0) command += SEP + id + SEP + cart[id];

  if (command === 'order') return;

  act(command);
  cart = null;
});

document.getElementById('order-close').addEventListener('click', closeSheet);

function closeSheet() {
  draft = null;
  cart = null;
  picker.open = false;

  act('sheet-close');
}

// ---- when the deal should land ---------------------------------------------------------------------------------

// Saying yes to a customer does not close the deal - it asks WHEN. The game opens its own picker and keeps the
// callback that files the contract, so this draws the four windows and presses the real one.
// Where each window sits on the dial. Noon is at the top and midnight at the bottom, so morning fills the top left
// and the day runs clockwise from there - the order the game's own enum has them in.
var DIAL = ['tl', 'tr', 'br', 'bl'];

var WHEEL_R = 137.4;          // half of the 450-unit wheel, in css px

function renderDeal() {
  var d = sheets.deal;
  if (!d) return;

  document.getElementById('deal-now').textContent = d.now;

  var wheel = document.getElementById('deal-wheel');

  // The quadrants are rebuilt; the cross, the marker and the middle stay where the markup put them.
  var old = wheel.querySelectorAll('.dw-q');
  for (var k = 0; k < old.length; k++) wheel.removeChild(old[k]);

  for (var i = 0; i < d.windows.length && i < DIAL.length; i++) {
    var w = d.windows[i];

    // Vanilla darkens a window with less than two hours left rather than hiding it - what is closed today is open
    // again tomorrow, and saying so is more use than a smaller dial.
    var q = document.createElement('div');
    q.className = 'dw-q ' + DIAL[i] + (w.open ? '' : ' off');
    q.setAttribute('data-i', String(i));

    q.appendChild(text('dw-q-name', w.name));
    q.appendChild(text('dw-q-span', w.span));

    // The wash that marks a closed window. Always present, shown by the class - a page that adds and removes
    // elements per state ends up with two ways of being in the same state.
    var shade = document.createElement('div');
    shade.className = 'dw-shade';
    q.appendChild(shade);

    if (w.open) q.addEventListener('click', dealClicked);

    // Before the cross and the marker, so those stay on top of it.
    wheel.insertBefore(q, wheel.firstChild);
  }

  placeArm(d.minutes);
}

// The marker on the rim, one turn a day, clockwise from midnight at the bottom.
//
// Positioned by hand rather than by rotating a full-size box, because the two renderers disagree about what a box
// rotates around: the game turns it about its top-left, a browser about its middle. Working out the top-left that
// puts the marker's centre in the right place is the one form both agree on - the same trick the contacts graph
// uses for its edges.
function placeArm(minutes) {
  var arm = document.getElementById('deal-arm');
  if (!arm) return;

  var deg = (minutes % 1440) / 1440 * 360;
  var rad = deg * Math.PI / 180;

  var cx = WHEEL_R - WHEEL_R * Math.sin(rad);
  var cy = WHEEL_R + WHEEL_R * Math.cos(rad);

  var at = topLeftFor(cx, cy, 11, 42.7, deg);

  arm.setAttribute('style', 'left:' + at.x.toFixed(1) + 'px;top:' + at.y.toFixed(1)
                          + 'px;transform:rotate(' + deg.toFixed(1) + 'deg)');
}

// Where to put a rotated box's TOP-LEFT so that its centre lands at (cx, cy).
function topLeftFor(cx, cy, w, h, deg) {
  var rad = deg * Math.PI / 180;
  var cos = Math.cos(rad);
  var sin = Math.sin(rad);

  return {
    x: cx - (w / 2 * cos - h / 2 * sin),
    y: cy - (w / 2 * sin + h / 2 * cos),
  };
}

function dealClicked(e) {
  act('deal' + SEP + e.currentTarget.getAttribute('data-i'));
}

document.getElementById('deal-close').addEventListener('click', closeSheet);

// ---- back and forth --------------------------------------------------------------------------------------------

document.getElementById('back').addEventListener('click', toList);

function toList() {
  openId = '';
  open = null;
  seenThread = false;
  show();
  renderList();
}

// Right-click and Escape mean back, one layer at a time - the picker, then the game's window, then the sheet, then
// the conversation. Only with the list already showing does the phone get to close the app.
document.addEventListener('back', function (e) {
  if (picker.open) { e.preventDefault(); picker.open = false; show(inSheet()); return; }
  if (sheets.counter || sheets.order || sheets.deal) { e.preventDefault(); closeSheet(); return; }
  if (inSheet()) { e.preventDefault(); show(false); return; }
  if (openId) { e.preventDefault(); toList(); }
});

function act(command) {
  var reply = s1.call('reflash-messages.act', command);
  if (reply && reply.indexOf('err:') === 0) s1.log('refused: ' + reply);

  refresh();
}

function refresh() {
  var sheet = inSheet();

  readList();
  readThread();
  renderList();

  // A conversation that is not there YET is not a conversation that is gone.
  //
  // In the game s1.call answers on the spot; on a second screen the first read of a value comes back empty and
  // arrives a moment later. Treating that empty answer as "the thread disappeared" threw the player straight back
  // to the list every time they opened one - and every reply they pressed bounced them out before the answer
  // landed. So the screen is only given up once the mod has actually said the thread is gone.
  if (openId && !open) {
    if (seenThread) { toList(); }
    return;
  }

  if (openId) seenThread = true;

  show(sheet && hasCompose());
  if (openId) renderThread();
  if (sheet && hasCompose()) renderSheet();

  renderCounter();
  renderOrder();
  renderDeal();
}

s1.on('reflash-messages.changed', refresh);

// ---- helpers ---------------------------------------------------------------------------------------------------

function div(className) {
  var el = document.createElement('div');
  el.className = className;
  return el;
}

function text(className, value) {
  var el = document.createElement('div');
  el.className = className;
  el.textContent = value;
  return el;
}

// setAttribute, not img.src - assigning to the property does nothing here, and a picture with no source paints an
// empty box rather than failing loudly.
function picture(src) {
  var img = document.createElement('img');
  img.setAttribute('src', src);
  return img;
}

function find(items, id) {
  if (!items) return null;
  for (var i = 0; i < items.length; i++) if (items[i].id === id) return items[i];
  return null;
}

function clamp(value, low, high) {
  return value < low ? low : value > high ? high : value;
}

// Game text carries TextMeshPro markup: a price arrives as `<color=#46CB4F>$80</color>`, and vanilla draws it
// green. This page cannot hand that to a text node - the renderer escapes a literal '<' in page text on purpose,
// because page text is content and not markup - so the tags would print verbatim.
//
// Turning them into real spans is what keeps the colour. The renderer folds inline children back into one
// TextMeshPro string with a colour tag derived from the CSS, so the result is what vanilla shows, and a browser
// renders the same spans natively.
//
// The FIRST run is deliberately left as bare text rather than wrapped: the renderer only treats an element as a
// run of text when it has direct text of its own, and a box of nothing but spans would stack them vertically.
function rich(el, value) {
  el.replaceChildren();
  if (!value) return;

  if (value.indexOf('<') < 0) { el.textContent = value; return; }

  var out = '';
  var at = 0;
  var colour = '';
  var tag = /<(\/?)([a-zA-Z]+)(?:=([^>]*))?>/g;
  var m;

  while ((m = tag.exec(value)) !== null) {
    out += run(value.substring(at, m.index), colour);
    at = tag.lastIndex;

    // Anything that is not a colour is dropped rather than guessed at - <b> and <i> would need a matching span
    // each, and the game only ever colours.
    if (m[2].toLowerCase() !== 'color') continue;

    colour = m[1] ? '' : (m[3] || '');
  }

  out += run(value.substring(at), colour);
  el.innerHTML = out;
}

function run(chunk, colour) {
  if (!chunk) return '';

  var safe = chunk.split('&').join('&amp;').split('<').join('&lt;').split('>').join('&gt;');
  return colour ? '<span style="color:' + colour + '">' + safe + '</span>' : safe;
}

readList();
renderList();
