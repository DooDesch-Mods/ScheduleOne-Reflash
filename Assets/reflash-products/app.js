// Products. Rebuilt against the real screen - vanilla-dumps/vanilla-products.txt.
//
// Vanilla groups the products by drug type, each group under its own coloured title, as 100x100 tiles whose frame
// says whether the product is listed. Favourites come first. The detail column sits on the LEFT.
//
// The price field commits on ENTER or with the step buttons: the renderer dispatches no blur and no change, so
// "leave the field and it takes the value" does not exist here and has to be something you can see.

var SEP = String.fromCharCode(31);

// A screen is budgeted at roughly 100 boxes and hard-capped near 200 - a rebuild costs about half a millisecond
// per box. A tile is three boxes, so this many keeps a page inside the budget with room for the chrome.
var PAGE = 36;

var appEl = document.getElementById('app');
var listEl = document.getElementById('products');
var emptyEl = document.getElementById('empty');
var pagerEl = document.getElementById('pager');
var countEl = document.getElementById('count');
var detailEl = document.getElementById('detail');
var instructionEl = document.getElementById('instruction');

var state = { rev: 0, products: [] };
var detail = null;
var selectedId = null;
var page = 0;

// The drug types vanilla names, with the class that carries each title colour.
// In vanilla's own order, which is not alphabetical, and every one of them is shown whether or not the player has
// discovered anything in it - an empty heading with "None" under it is how the app says what is still to come.
var CATEGORIES = [
  { key: 'Marijuana', label: 'Marijuana', cls: 'marijuana' },
  { key: 'Methamphetamine', label: 'Methamphetamine', cls: 'meth' },
  { key: 'Shrooms', label: 'Shrooms', cls: 'shrooms' },
  { key: 'Cocaine', label: 'Cocaine', cls: 'cocaine' },
];

function readList() {
  state = parse(s1.call('reflash-products.state', ''), { rev: 0, products: [] });
  if (!state.products) state.products = [];
}

function readDetail() {
  if (!selectedId) { detail = null; return; }

  var data = parse(s1.call('reflash-products.state', 'item:' + selectedId), null);
  detail = data && data.product ? data.product : null;
  if (data && typeof data.rev === 'number') state.rev = data.rev;
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
  emptyEl.className = state.products.length ? 'empty' : 'empty on';

  // One group per drug type, always, with favourites first inside their own type. Vanilla has no separate
  // favourites heading - a product only ever appears once.
  var used = {};
  for (var c = 0; c < CATEGORIES.length; c++) {
    var cat = CATEGORIES[c];
    var members = state.products.filter(function (p) { return p.quality === cat.key; });

    members.sort(function (a, b) { return (b.fav ? 1 : 0) - (a.fav ? 1 : 0); });
    for (var m = 0; m < members.length; m++) used[members[m].id] = true;

    group(cat.label, cat.cls, members);
  }

  // Anything whose type this page does not know about still has to appear - a silent omission would look like a
  // missing product rather than a missing category.
  var rest = state.products.filter(function (p) { return !used[p.id]; });
  if (rest.length) group('Other', 'other', rest);

  renderPager();
}

function group(label, cls, products) {
  var title = document.createElement('div');
  title.className = 'cat-title ' + cls;
  title.textContent = label;
  listEl.appendChild(title);

  if (!products.length) {
    var none = document.createElement('div');
    none.className = 'cat-none';
    none.textContent = 'None';
    listEl.appendChild(none);
    return;
  }

  var tiles = document.createElement('div');
  tiles.className = 'tiles';

  var max = Math.min(products.length, PAGE);
  for (var i = 0; i < max; i++) tiles.appendChild(tile(products[i]));

  listEl.appendChild(tiles);

  if (products.length > max) {
    var more = document.createElement('div');
    more.className = 'cat-none';
    more.textContent = 'and ' + (products.length - max) + ' more';
    listEl.appendChild(more);
  }
}

function tile(p) {
  var el = document.createElement('div');
  el.className = 'tile' + (p.listed ? ' listed' : '') + (p.id === selectedId ? ' on' : '');
  el.setAttribute('data-id', p.id);

  // Vanilla puts a star on every tile and fills it in for a favourite - it is not only there once it is on.
  var star = document.createElement('button');
  star.className = 'star' + (p.fav ? ' on' : '');
  star.setAttribute('data-id', p.id);

  var glyph = document.createElement('img');
  glyph.setAttribute('src', 'icon-star.png');
  star.appendChild(glyph);

  star.addEventListener('click', function (e) {
    if (e.stopPropagation) e.stopPropagation();
    act('fav' + SEP + p.id + SEP + (p.fav ? '0' : '1'));
  });

  el.appendChild(star);

  // Vanilla's tile is the product's own icon and nothing else - the name lives in the detail panel. The picture
  // arrives a few per tick, so a tile falls back to its name until then rather than opening empty.
  if (p.icon) {
    var icon = document.createElement('img');
    icon.className = 'tile-icon';
    icon.setAttribute('src', 's1://icon-' + p.id);
    icon.setAttribute('width', '49');
    icon.setAttribute('height', '49');
    el.appendChild(icon);
  } else {
    var name = document.createElement('div');
    name.className = 'tile-name';
    name.textContent = p.name;
    el.appendChild(name);
  }

  // The corner badge says whether the product is for sale, not what it costs: a green dollar when it is listed, a
  // red cross when it is not. Vanilla puts the price in the detail panel and nowhere else.
  var badge = document.createElement('div');
  badge.className = 'tile-badge';
  badge.textContent = p.listed ? '$' : 'x';
  el.appendChild(badge);

  el.addEventListener('click', pick);
  return el;
}

function renderPager() {
  pagerEl.replaceChildren();
}

function pick(e) {
  selectedId = e.currentTarget.getAttribute('data-id');
  readDetail();
  appEl.classList.add('show-detail');
  render();
}

function renderDetail() {
  detailEl.replaceChildren();
  instructionEl.className = detail ? 'instruction' : 'instruction on';

  if (!detail) return;

  var body = document.createElement('div');
  body.className = 'detail-body';

  // Vanilla's own order and wording: the name, the asking price beside its suggestion, the sale toggle as a
  // question with a tick, the effects as a bulleted list, and the addictiveness as a bar with its reading under it.
  body.appendChild(text('detail-name', detail.name));

  body.appendChild(priceField());
  body.appendChild(saleRow());
  body.appendChild(bullets('Effects', detail.properties));
  body.appendChild(addictiveness());

  detailEl.appendChild(body);
}

/// One row, as vanilla has it: the label, the field, and what the market says it is worth.
function priceField() {
  var row = document.createElement('div');
  row.className = 'price-row';

  row.appendChild(text('field-label', 'Asking Price'));

  var input = document.createElement('input');
  input.className = 'price-input';
  input.value = '$' + detail.price;
  // keydown is the only key event delivered, and only for Enter - which is exactly the commit gesture. There is no
  // blur and no change here, so "leave the field and it takes the value" does not exist and must not be implied.
  input.addEventListener('keydown', function (e) {
    if (e.key === 'Enter') setPrice(String(e.value).split('$').join(''));
  });
  row.appendChild(input);

  row.appendChild(text('suggested', 'Suggested: $' + detail.value));
  return row;
}

function stepButton(label, delta) {
  var btn = document.createElement('button');
  btn.className = 'step';
  btn.textContent = label;
  btn.addEventListener('click', function () { setPrice(String(detail.price + delta)); });
  return btn;
}

function setPrice(value) {
  var n = parseInt(value, 10);
  if (isNaN(n)) return;
  if (n < 0) n = 0;

  act('price' + SEP + detail.id + SEP + String(n));
}

/// "List For Sale?" with a tick after it - a question and its answer, which is how vanilla puts it. The star is
/// this app's own: vanilla marks a favourite on the tile, and the tile is where it is shown.
function saleRow() {
  var row = document.createElement('div');
  row.className = 'sale-row';

  row.appendChild(text('field-label', 'List For Sale?'));

  // A picture, not a letter: the game's text atlases carry Latin-1 and have no check mark, and a 'Y' is visibly
  // not what vanilla draws.
  var tick = document.createElement('button');
  tick.className = 'tick' + (detail.listed ? ' on' : '');

  var mark = document.createElement('img');
  mark.setAttribute('src', 'icon-check.png');
  tick.appendChild(mark);

  tick.addEventListener('click', function () {
    act('list' + SEP + detail.id + SEP + (detail.listed ? '0' : '1'));
  });
  row.appendChild(tick);

  return row;
}

/// The effects, one per line with a bullet, in the green vanilla writes them in.
function bullets(label, values) {
  var field = document.createElement('div');
  field.className = 'field';
  field.appendChild(text('field-label', label));

  var list = document.createElement('div');
  list.className = 'chiplist';

  if (!values || !values.length) {
    list.appendChild(text('prop none', 'None'));
  } else {
    // Each effect in its own colour, which is the game's, not a guess - vanilla writes the name in the effect's
    // LabelColor and the mod sends that colour along with the name.
    // A middle dot, which the font atlases DO carry - a bullet at U+2022 would draw as an empty box.
    for (var i = 0; i < values.length; i++) {
      var line = text('prop', '· ' + values[i].text);
      if (values[i].colour) line.setAttribute('style', 'color:' + values[i].colour);
      list.appendChild(line);
    }
  }

  field.appendChild(list);
  return field;
}

/// A bar that runs white to red, with the percentage under it - vanilla's own shape for this number.
function addictiveness() {
  var field = document.createElement('div');
  field.className = 'field';
  field.appendChild(text('field-label', 'Addictiveness'));

  var bar = document.createElement('div');
  bar.className = 'bar';

  var mark = document.createElement('div');
  mark.className = 'bar-mark';
  mark.setAttribute('style', 'left:' + Math.round(Math.max(0, Math.min(100, detail.addictiveness)) * 0.98) + '%');
  bar.appendChild(mark);

  field.appendChild(bar);
  field.appendChild(text('bar-value', detail.addictiveness + '%'));
  return field;
}

function chips(label, values) {
  var field = document.createElement('div');
  field.className = 'field';
  field.appendChild(text('field-label', label));

  var list = document.createElement('div');
  list.className = 'chiplist';
  for (var i = 0; i < values.length; i++) {
    var chip = document.createElement('div');
    chip.className = 'prop';
    chip.textContent = values[i];
    list.appendChild(chip);
  }

  field.appendChild(list);
  return field;
}

function text(cls, value) {
  var el = document.createElement('div');
  el.className = cls;
  el.textContent = value;
  return el;
}

function act(command) {
  s1.call('reflash-products.act', command);
  readList();
  readDetail();
  render();
}

document.getElementById('back').addEventListener('click', toList);

document.addEventListener('back', function (e) {
  if (s1.orientation !== 'portrait') return;
  if (!appEl.classList.contains('show-detail')) return;

  e.preventDefault();
  toList();
});

function toList() {
  appEl.classList.remove('show-detail');
  selectedId = null;
  detail = null;
  render();
}

s1.on('reflash-products.changed', function () {
  readList();
  readDetail();
  render();
});

readList();
render();
