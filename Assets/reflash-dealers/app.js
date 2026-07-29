// Dealers. Pick one, see what they hold, move customers on and off them, set their cut.
//
// Assigning a customer is a two-step "pick from a list" rather than a drag: the renderer delivers no pointermove,
// so dragging is not available at all. With a mouse in a 733px window the list is arguably the better gesture
// anyway - and it is the only one a controller can use.

var SEP = String.fromCharCode(31);

var appEl = document.getElementById('app');
var listEl = document.getElementById('dealers');
var emptyEl = document.getElementById('empty');
var detailEl = document.getElementById('detail');
var titleEl = document.getElementById('detail-title');

var dealers = [];
var detail = null;
var selectedId = null;
var picking = false;
var candidates = [];

function readList() {
  var data = parse(s1.call('reflash-dealers.state', ''), null);
  dealers = data && data.dealers ? data.dealers : [];
}

function readDetail() {
  if (!selectedId) { detail = null; return; }

  var data = parse(s1.call('reflash-dealers.state', 'dealer:' + selectedId), null);
  detail = data ? data : null;
}

function readCandidates() {
  var data = parse(s1.call('reflash-dealers.state', 'assignable:' + selectedId), null);
  candidates = data && data.candidates ? data.candidates : [];
}

function parse(raw, fallback) {
  if (!raw) return fallback;
  try { return JSON.parse(raw); } catch (e) { s1.log('state did not parse: ' + e); return fallback; }
}

function render() {
  renderList();
  renderDetail();
}

function renderList() {
  listEl.replaceChildren();
  emptyEl.className = dealers.length ? 'empty' : 'empty on';

  for (var i = 0; i < dealers.length; i++) {
    var d = dealers[i];

    var row = document.createElement('div');
    row.className = 'dealer' + (d.id === selectedId ? ' on' : '');
    row.setAttribute('data-id', d.id);

    var name = document.createElement('div');
    name.className = 'dealer-name';
    name.textContent = d.name;
    row.appendChild(name);

    var sub = document.createElement('div');
    sub.className = 'dealer-sub';

    var region = document.createElement('div');
    region.textContent = d.region;
    sub.appendChild(region);

    var count = document.createElement('div');
    count.textContent = d.customers + '/' + d.limit;
    sub.appendChild(count);

    row.appendChild(sub);
    row.addEventListener('click', pick);
    listEl.appendChild(row);
  }
}

function pick(e) {
  selectedId = e.currentTarget.getAttribute('data-id');
  picking = false;
  readDetail();
  appEl.classList.add('show-detail');
  render();
}

function renderDetail() {
  detailEl.replaceChildren();

  if (!detail || !detail.dealer) {
    titleEl.textContent = 'Dealer';
    detailEl.appendChild(text('hint on', dealers.length ? 'Pick a dealer.' : ''));
    return;
  }

  var d = detail.dealer;
  titleEl.textContent = d.name;

  var body = document.createElement('div');
  body.className = 'detail-body';

  body.appendChild(stats(d));
  body.appendChild(cutField(d));
  body.appendChild(customers(d));
  if (detail.inventory && detail.inventory.length) body.appendChild(inventory());

  detailEl.appendChild(body);
}

function stats(d) {
  var row = document.createElement('div');
  row.className = 'stats';
  row.appendChild(stat('Cash', '$' + d.cash, 'cash'));
  row.appendChild(stat('Cut', d.cut + '%', 'cut'));
  row.appendChild(stat('Region', d.region, 'plain'));
  return row;
}

function stat(label, value, kind) {
  var box = document.createElement('div');
  box.className = 'stat ' + (kind || 'plain');
  box.appendChild(text('stat-label', label));
  box.appendChild(text('stat-value', value));
  return box;
}

function cutField(d) {
  var section = document.createElement('div');
  section.className = 'section';
  section.appendChild(text('section-label', 'Cut'));

  var row = document.createElement('div');
  row.className = 'cut-row';

  row.appendChild(cutButton('-5', d.cut - 5));
  row.appendChild(text('cut-value', d.cut + '%'));
  row.appendChild(cutButton('+5', d.cut + 5));

  section.appendChild(row);
  return section;
}

function cutButton(label, value) {
  var btn = document.createElement('button');
  btn.className = 'step';
  btn.textContent = label;
  btn.addEventListener('click', function () {
    if (value < 0 || value > 100) return;
    act('cut' + SEP + selectedId + SEP + String(value));
  });
  return btn;
}

function customers(d) {
  var section = document.createElement('div');
  section.className = 'section';
  section.appendChild(text('section-label', 'Customers ' + d.customers + '/' + d.limit));

  var list = detail.customers || [];
  for (var i = 0; i < list.length; i++) section.appendChild(customerRow(list[i]));

  if (picking) section.appendChild(picker());
  else section.appendChild(addButton(d));

  return section;
}

function customerRow(c) {
  var row = document.createElement('div');
  row.className = 'entry';

  var name = document.createElement('div');
  name.className = 'entry-name';
  name.textContent = c.name;
  row.appendChild(name);

  row.appendChild(text('entry-sub', c.relLabel));

  var drop = document.createElement('button');
  drop.className = 'drop';
  drop.textContent = 'Remove';
  drop.addEventListener('click', function () { act('remove' + SEP + selectedId + SEP + c.id); });
  row.appendChild(drop);

  return row;
}

function addButton(d) {
  var full = d.customers >= d.limit;

  var btn = document.createElement('button');
  btn.className = 'add' + (full ? ' off' : '');
  btn.textContent = full ? 'Full' : 'Add customer';

  // A full dealer gets a disabled-looking button rather than a hidden one - "why can I not add anyone" is a
  // question the screen should answer without being asked.
  if (!full) btn.addEventListener('click', function () {
    picking = true;
    readCandidates();
    render();
  });

  return btn;
}

function picker() {
  var wrap = document.createElement('div');
  wrap.className = 'picker';

  if (!candidates.length) {
    wrap.appendChild(text('entry-sub', 'Nobody free to assign.'));
    return wrap;
  }

  // Capped: the picker is a choice, not a directory, and a save can carry a lot of customers.
  var max = Math.min(candidates.length, 12);
  for (var i = 0; i < max; i++) {
    var c = candidates[i];

    var row = document.createElement('div');
    row.className = 'candidate';
    row.setAttribute('data-id', c.id);

    var name = document.createElement('div');
    name.className = 'entry-name';
    name.textContent = c.name;
    row.appendChild(name);

    row.appendChild(text('entry-sub', c.region));
    row.addEventListener('click', assign);
    wrap.appendChild(row);
  }

  if (candidates.length > max)
    wrap.appendChild(text('entry-sub', 'and ' + (candidates.length - max) + ' more'));

  return wrap;
}

function assign(e) {
  act('add' + SEP + selectedId + SEP + e.currentTarget.getAttribute('data-id'));
}

function inventory() {
  var section = document.createElement('div');
  section.className = 'section';
  section.appendChild(text('section-label', 'Carrying'));

  for (var i = 0; i < detail.inventory.length; i++) {
    var slot = detail.inventory[i];

    var row = document.createElement('div');
    row.className = 'entry';

    var name = document.createElement('div');
    name.className = 'entry-name';
    name.textContent = slot.name;
    row.appendChild(name);

    row.appendChild(text('entry-sub', 'x' + slot.qty));
    section.appendChild(row);
  }

  return section;
}

function text(cls, value) {
  var el = document.createElement('div');
  el.className = cls;
  el.textContent = value;
  return el;
}

function act(command) {
  s1.call('reflash-dealers.act', command);
  picking = false;
  readList();
  readDetail();
  render();
}

document.getElementById('back').addEventListener('click', toList);

document.addEventListener('back', function (e) {
  if (s1.orientation !== 'portrait') return;

  // The picker is a step of its own, so back closes that first rather than leaving the dealer.
  if (picking) { e.preventDefault(); picking = false; render(); return; }
  if (!appEl.classList.contains('show-detail')) return;

  e.preventDefault();
  toList();
});

function toList() {
  appEl.classList.remove('show-detail');
  selectedId = null;
  detail = null;
  picking = false;
  render();
}

s1.on('reflash-dealers.changed', function () {
  readList();
  readDetail();
  render();
});

readList();
render();
