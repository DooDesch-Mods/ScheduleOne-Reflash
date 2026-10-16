// Dealer Management. One dealer at a time, picked from the dropdown: their cash and cut, what they carry, and the
// customers they sell to, each with a cross to take them off. Under ten customers, "Assign Customer" lists the
// ones nobody sells to yet.
//
// Assigning is a pick from a list rather than vanilla's customer selector screen: the renderer delivers no
// pointermove, and a list is the gesture a controller can use too.

var SEP = String.fromCharCode(31);

var bodyEl = document.getElementById('body');
var emptyEl = document.getElementById('empty');
var optionsEl = document.getElementById('options');

var dealers = [];
var detail = null;
var selectedId = null;
var open = false;
var picking = false;
var candidates = [];

function parse(raw, fallback) {
  if (!raw) return fallback;
  try { return JSON.parse(raw); } catch (e) { s1.log('state did not parse: ' + e); return fallback; }
}

function readList() {
  var data = parse(s1.call('reflash-dealers.state', ''), null);
  dealers = data && data.dealers ? data.dealers : [];

  // Vanilla shows the first dealer until another is picked, and the picked one until it is gone.
  var still = false;
  for (var i = 0; i < dealers.length; i++) if (dealers[i].id === selectedId) still = true;
  if (!still) selectedId = dealers.length ? dealers[0].id : null;
}

function readDetail() {
  if (!selectedId) { detail = null; return; }

  var data = parse(s1.call('reflash-dealers.state', 'dealer:' + selectedId), null);
  detail = data && data.dealer ? data : null;
}

function readCandidates() {
  var data = parse(s1.call('reflash-dealers.state', 'assignable:' + selectedId), null);
  candidates = data && data.candidates ? data.candidates : [];
}

function render() {
  bodyEl.replaceChildren();
  emptyEl.className = dealers.length ? 'empty' : 'empty on';
  renderOptions();

  if (!detail) return;

  var d = detail.dealer;
  bodyEl.appendChild(dropdown(d));
  bodyEl.appendChild(details(d));
  bodyEl.appendChild(inventory());
  bodyEl.appendChild(div('customer-title', 'Assigned Customers (' + d.customers + '/' + d.limit + ')'));
  bodyEl.appendChild(customers(d));
}

// ---- the dropdown -------------------------------------------------------------------------------------------

function dropdown(d) {
  var box = div('block dropdown' + (open ? ' open' : ''));
  if (d.face) box.appendChild(picture('face', 's1://face-' + d.id));
  box.appendChild(nameLabel(d.name, d.region));
  box.appendChild(picture('drop-arrow', 'icon-dropdown.png'));
  box.addEventListener('click', function () { open = !open; render(); });
  return box;
}

function renderOptions() {
  optionsEl.replaceChildren();
  optionsEl.className = open && dealers.length ? 'options on' : 'options';
  if (!open) return;

  for (var i = 0; i < dealers.length; i++) {
    var d = dealers[i];

    var row = div('option' + (d.id === selectedId ? ' on' : ''));
    row.setAttribute('data-id', d.id);
    if (d.face) row.appendChild(picture('face', 's1://face-' + d.id));
    row.appendChild(nameLabel(d.name, d.region));
    row.addEventListener('click', choose);
    optionsEl.appendChild(row);
  }
}

function choose(e) {
  selectedId = e.currentTarget.getAttribute('data-id');
  open = false;
  picking = false;
  readDetail();
  render();
}

// ---- cash and cut -------------------------------------------------------------------------------------------

function details(d) {
  var row = div('details');
  row.appendChild(figure('Cash', money(d.cash), 'cash'));
  row.appendChild(figure('Cut', d.cut + '%', 'cut'));
  return row;
}

function figure(title, value, cls) {
  var box = div('block detail');
  box.appendChild(div('', title));
  box.appendChild(div(cls, value));
  return box;
}

// ---- inventory ------------------------------------------------------------------------------------------------

// The name's share of an item, 226 units, which vanilla shrinks its text to fit.
var ITEM_TEXT_W = 138;

// Vanilla's quality colours, Trash to Heavenly - the same five stars the customers' standards use.
var QUALITY = ['7d3232', '509132', '64beff', 'e14bff', 'ffc832'];

function inventory() {
  var box = div('block inventory');
  var items = detail.inventory || [];

  if (!items.length) {
    box.appendChild(div('no-items', 'No Items in inventory'));
    return box;
  }

  for (var i = 0; i < items.length; i++) {
    var it = items[i];

    var row = div('item');
    var icon = div('item-icon');
    if (it.icon) icon.appendChild(picture('pic', 's1://item-' + it.id));
    if (it.quality >= 0 && it.quality < QUALITY.length)
      icon.appendChild(picture('item-star', 'star-' + QUALITY[it.quality] + '.png'));
    row.appendChild(icon);
    row.appendChild(fitted(div('item-name', it.qty + 'x ' + it.name), ITEM_TEXT_W, 17.1));
    box.appendChild(row);
  }

  return box;
}

// ---- customers ----------------------------------------------------------------------------------------------

function customers(d) {
  var list = div('customers');
  var assigned = detail.customers || [];

  for (var i = 0; i < assigned.length; i++) list.appendChild(customerRow(assigned[i]));

  if (d.customers < d.limit) {
    if (picking) list.appendChild(picker());
    else {
      var assign = div('block assign', 'Assign Customer');
      assign.addEventListener('click', function () { picking = true; readCandidates(); render(); });
      list.appendChild(assign);
    }
  }

  return list;
}

function customerRow(c) {
  var row = div('block customer');
  row.appendChild(faceWithStar(c));
  row.appendChild(nameLabel(c.name, c.region));

  var remove = picture('remove', 'icon-remove.png');
  remove.addEventListener('click', function () { act('remove' + SEP + selectedId + SEP + c.id); });
  row.appendChild(remove);

  return row;
}

function faceWithStar(c) {
  var face = div('customer-face');
  if (c.face) face.appendChild(picture('pic', 's1://face-' + c.id));
  if (c.standards) face.appendChild(picture('customer-star', 'star-' + c.standards.replace('#', '') + '.png'));
  return face;
}

function picker() {
  var wrap = div('picker');

  if (!candidates.length) {
    wrap.appendChild(div('block assign faded', 'Nobody free to assign'));
    return wrap;
  }

  for (var i = 0; i < candidates.length; i++) {
    var c = candidates[i];

    var row = div('block candidate');
    row.setAttribute('data-id', c.id);
    row.appendChild(faceWithStar(c));
    row.appendChild(nameLabel(c.name, c.region));
    row.addEventListener('click', assign);
    wrap.appendChild(row);
  }

  return wrap;
}

function assign(e) {
  act('add' + SEP + selectedId + SEP + e.currentTarget.getAttribute('data-id'));
}

// ---- helpers ----------------------------------------------------------------------------------------------------

// "Kyle Cooley (Northtown)", the region in vanilla's faded text colour.
function nameLabel(name, region) {
  var label = div('label');
  label.appendChild(div('', name));
  if (region) label.appendChild(div('faded region', '(' + region + ')'));
  return label;
}

// Vanilla's auto-sizing text, by estimate: the page cannot measure a string, and an Open Sans SemiBold letter runs
// about 0.56 of the font size wide.
function fitted(el, width, size) {
  var length = (el.textContent || '').length;
  var fit = length ? width / (length * 0.56) : size;
  if (fit < size) el.style.fontSize = Math.max(8, fit).toFixed(1) + 'px';
  return el;
}

function div(cls, value) {
  var el = document.createElement('div');
  if (cls) el.className = cls;
  if (value !== undefined) el.textContent = value;
  return el;
}

// "$28,727", the way MoneyManager.FormatAmount writes it.
function money(value) {
  var n = Math.round(value || 0);
  var digits = String(Math.abs(n));
  var out = '';

  for (var i = 0; i < digits.length; i++) {
    if (i > 0 && (digits.length - i) % 3 === 0) out += ',';
    out += digits[i];
  }

  return (n < 0 ? '-$' : '$') + out;
}

// setAttribute, not img.src - assigning to the property does nothing here.
function picture(cls, src) {
  var img = document.createElement('img');
  if (cls) img.className = cls;
  img.setAttribute('src', src);
  return img;
}

function act(command) {
  var reply = s1.call('reflash-dealers.act', command);
  if (reply && reply !== 'ok') s1.log('dealers: "' + command.split(SEP).join(' ') + '" answered ' + reply);

  picking = false;
  readList();
  readDetail();
  render();
}

// Back closes what is open on top first: the dropdown's list, then the customer list.
document.addEventListener('back', function (e) {
  if (open) { e.preventDefault(); open = false; render(); return; }
  if (picking) { e.preventDefault(); picking = false; render(); }
});

s1.on('reflash-dealers.changed', function () {
  readList();
  readDetail();
  if (picking) readCandidates();
  render();
});

readList();
readDetail();
render();
