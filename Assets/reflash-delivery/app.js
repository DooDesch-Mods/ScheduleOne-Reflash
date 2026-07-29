// Deliveries. Rebuilt against the real screen - vanilla-dumps/vanilla-deliveries.txt.
//
// Vanilla lists the shops as wide coloured cards and opens one to order from it, so this does the same: shop list,
// then the listings of the shop you picked. The whole order goes over as ONE command, because the game's submit is
// one transaction - fee, money, delivery and receipt together - and sending lines one at a time would produce one
// delivery per item.

var SEP = String.fromCharCode(31);

var bodyEl = document.getElementById('body');
var basketEl = document.getElementById('basket');
var balanceEl = document.getElementById('balance');
var activeCountEl = document.getElementById('active-count');

var tab = 'shops';
var shops = { shops: [], balance: 0, active: 0 };
var deliveries = [];
var basket = {};        // listingId -> quantity
var openShop = null;    // the shop being ordered from, or null while the card list is showing
var note = '';

function readShops() {
  shops = parse(s1.call('reflash-delivery.state', ''), { shops: [], balance: 0, active: 0 });
  if (!shops.shops) shops.shops = [];
}

function readDeliveries(which) {
  var data = parse(s1.call('reflash-delivery.state', which), null);
  deliveries = data && data.deliveries ? data.deliveries : [];
}

function parse(raw, fallback) {
  if (!raw) return fallback;
  try { return JSON.parse(raw); } catch (e) { s1.log('state did not parse: ' + e); return fallback; }
}

function refresh() {
  readShops();
  if (tab !== 'shops') readDeliveries(tab);
  render();
}

function render() {
  balanceEl.textContent = '$' + shops.balance;
  activeCountEl.textContent = shops.active ? '(' + shops.active + ')' : '';

  bodyEl.replaceChildren();
  if (note) bodyEl.appendChild(text('note', note));

  if (tab !== 'shops') renderDeliveries();
  else if (openShop) renderListings();
  else renderShops();

  renderBasket();
}

function renderShops() {
  if (!shops.shops.length) { bodyEl.appendChild(text('empty', 'No shops deliver yet.')); return; }

  // Two cards to a row, so they live in a wrapping strip of their own rather than straight in the scroll box.
  var grid = document.createElement('div');
  grid.className = 'shops';

  for (var i = 0; i < shops.shops.length; i++) grid.appendChild(shopCard(shops.shops[i]));
  bodyEl.appendChild(grid);
}

function shopCard(shop) {
  var card = document.createElement('div');
  card.className = 'shop';
  card.setAttribute('data-id', shop.id);

  // The colour is authored per shop in the game, not derived from its name - reading it is the difference between
  // matching vanilla and matching whatever "Gas" happens to appear in.
  if (shop.colour) card.setAttribute('style', 'background:' + shop.colour);

  // Only where there is one. Two of the four vanilla cards carry no portrait at all, and an empty circle in its
  // place is something vanilla does not draw.
  if (shop.icon) {
    var icon = document.createElement('div');
    icon.className = 'shop-icon';

    var face = document.createElement('img');
    face.setAttribute('src', 's1://shop-' + shop.iconKey);
    icon.appendChild(face);

    card.appendChild(icon);
  }

  var textBox = document.createElement('div');
  textBox.className = 'shop-text';

  var title = document.createElement('div');
  title.className = 'shop-title';
  title.textContent = shop.name;
  textBox.appendChild(title);

  var desc = document.createElement('div');
  desc.className = 'shop-desc';
  desc.textContent = shop.desc || (shop.listings.length + ' items');
  textBox.appendChild(desc);


  card.appendChild(textBox);

  // ">" rather than an arrow glyph - the font atlases carry Latin-1 and U+2192 draws as a box.
  var arrow = document.createElement('div');
  arrow.className = 'shop-arrow';
  arrow.textContent = '>';
  card.appendChild(arrow);

  card.addEventListener('click', function () {
    openShop = shop.id;
    basket = {};

    // Clear the game's own panel too. It keeps whatever was last put in it, so a shop opened after an abandoned
    // order showed that order's totals against a basket of nothing.
    s1.call('reflash-delivery.act', 'fill' + SEP + shop.id);

    refresh();
  });
  return card;
}

function renderListings() {
  var shop = findShop(openShop);
  if (!shop) { openShop = null; renderShops(); return; }

  var head = document.createElement('div');
  head.className = 'delivery-head';

  var back = document.createElement('button');
  back.className = 'back-btn';
  back.textContent = '< Back';
  back.addEventListener('click', function () { openShop = null; render(); });
  head.appendChild(back);

  if (shop.icon) {
    var face = document.createElement('div');
    face.className = 'head-face';

    var picture = document.createElement('img');
    picture.setAttribute('src', 's1://shop-' + shop.iconKey);
    face.appendChild(picture);

    head.appendChild(face);
  }

  var titles = document.createElement('div');
  titles.className = 'head-text';
  titles.appendChild(text('section-title', shop.name));
  if (shop.desc) titles.appendChild(text('shop-desc', shop.desc));
  head.appendChild(titles);
  bodyEl.appendChild(head);

  // The goods on the left in two columns, the totals down the right, exactly as vanilla splits this screen.
  var split = document.createElement('div');
  split.className = 'split';

  var grid = document.createElement('div');
  grid.className = 'listings';

  for (var i = 0; i < shop.listings.length; i++) grid.appendChild(listingRow(shop, shop.listings[i]));
  split.appendChild(grid);

  split.appendChild(summary(shop));
  bodyEl.appendChild(split);
}

function listingRow(shop, listing) {
  var row = document.createElement('div');
  row.className = 'listing' + (listing.locked ? ' locked' : '');

  var icon = document.createElement('div');
  icon.className = 'listing-icon';

  if (listing.icon) {
    var picture = document.createElement('img');
    picture.setAttribute('src', 's1://shop-' + listing.id);
    icon.appendChild(picture);
  }

  row.appendChild(icon);

  // The name over its price, then the stepper hard against the right edge - vanilla's arrangement.
  var textBox = document.createElement('div');
  textBox.className = 'listing-text';

  var name = document.createElement('div');
  name.className = 'listing-name';
  name.textContent = listing.name;
  textBox.appendChild(name);

  var price = document.createElement('div');
  price.className = 'listing-price' + (listing.afford ? '' : ' poor');
  price.textContent = '$' + listing.price;
  textBox.appendChild(price);
  row.appendChild(textBox);

  var stepper = document.createElement('div');
  stepper.className = 'stepper';
  stepper.appendChild(stepButton(shop, listing, -1, '-'));

  var qty = document.createElement('div');
  qty.className = 'qty';
  qty.textContent = String(basket[listing.id] || 0);
  stepper.appendChild(qty);

  // Vanilla puts a padlock where the plus would be for something the player has not ranked up to, and leaves the
  // row in place - the point is to show what is still to come.
  if (listing.locked) {
    var lock = document.createElement('div');
    lock.className = 'listing-lock';

    var glyph = document.createElement('img');
    glyph.setAttribute('src', 'icon-lock.png');
    lock.appendChild(glyph);

    stepper.appendChild(lock);
  } else {
    stepper.appendChild(stepButton(shop, listing, 1, '+'));
  }

  row.appendChild(stepper);
  return row;
}

/// The panel down the right: what the game's own order screen currently says, read off it rather than recomputed.
function summary(shop) {
  var panel = document.createElement('div');
  panel.className = 'summary';

  panel.appendChild(chooser(shop, 'Destination', shop.destinations, shop.destination, 'dest'));
  panel.appendChild(chooser(shop, 'Loading Dock', shop.docks, shop.dock, 'dock'));

  panel.appendChild(sum('Item Total', shop.itemTotal || '$0'));
  panel.appendChild(sum('Delivery Fee', shop.fee || '$0'));
  panel.appendChild(sum('Order Total', shop.orderTotal || '$0'));
  panel.appendChild(sum('Delivery Time', shop.time || '-', true));

  if (shop.note) panel.appendChild(text('sum-note', shop.note));

  var order = document.createElement('button');
  order.className = 'order' + (shop.canOrder ? '' : ' off');
  order.textContent = 'Place Order';
  order.addEventListener('click', submit);
  panel.appendChild(order);

  return panel;
}

/// A dropdown has no counterpart in the renderer - there is no select and no change event - so the choices are
/// laid out as a row of small buttons and the current one is marked. Same choices, same order, one tap.
function chooser(shop, label, options, index, op) {
  var block = document.createElement('div');
  block.className = 'chooser';
  block.appendChild(text('sum-k', label));

  var row = document.createElement('div');
  row.className = 'chooser-row';

  var values = options || [];
  for (var i = 0; i < values.length; i++) {
    var button = text('choice' + (i === index ? ' on' : ''), values[i]);
    button.setAttribute('data-i', String(i));
    button.addEventListener('click', function (e) {
      s1.call('reflash-delivery.act', op + SEP + shop.id + SEP + e.currentTarget.getAttribute('data-i'));
      refresh();
    });
    row.appendChild(button);
  }

  block.appendChild(row);
  return block;
}

function sum(label, value, plain) {
  var row = document.createElement('div');
  row.className = 'sum-row';
  row.appendChild(text('sum-k', label));
  row.appendChild(text('sum-v' + (plain ? ' plain' : ''), value));
  return row;
}

function stepButton(shop, listing, delta, label) {
  var btn = document.createElement('button');
  btn.className = 'step';
  btn.textContent = label;
  btn.addEventListener('click', function () { change(shop, listing, delta); });
  return btn;
}

function change(shop, listing, delta) {
  var next = (basket[listing.id] || 0) + delta;
  if (next <= 0) delete basket[listing.id];
  else basket[listing.id] = next;

  // Push the basket into the game's own panel first, so the fee and the total this page then reads are the ones
  // the game worked out rather than a guess made here.
  var command = 'fill' + SEP + shop.id;
  for (var id in basket) if (basket[id] > 0) command += SEP + id + SEP + basket[id];

  s1.call('reflash-delivery.act', command);
  refresh();
}

function renderBasket() {
  basketEl.replaceChildren();

  var ids = Object.keys(basket);
  // Only while browsing the shops. Inside one, the totals panel down the right says all of this and says it in the
  // game's own numbers.
  var show = ids.length && tab === 'shops' && !openShop;
  basketEl.className = 'basket' + (show ? ' on' : '');
  if (!show) return;

  var shop = findShop(openShop);
  var total = 0, count = 0;

  for (var i = 0; i < ids.length; i++) {
    var listing = shop ? findListing(shop, ids[i]) : null;
    if (!listing) continue;

    total += listing.price * basket[ids[i]];
    count += basket[ids[i]];
  }

  // "at least", because the game adds a delivery fee this page deliberately does not predict - that formula
  // belongs to the game and reproducing it here would go stale in a balance patch.
  basketEl.appendChild(text('basket-text', count + ' items, at least $' + total));

  var order = document.createElement('button');
  order.className = 'order';
  order.textContent = 'Order';
  order.addEventListener('click', submit);
  basketEl.appendChild(order);
}

function submit() {
  var command = 'order' + SEP + openShop;
  var ids = Object.keys(basket);

  for (var i = 0; i < ids.length; i++) command += SEP + ids[i] + SEP + basket[ids[i]];

  var reply = s1.call('reflash-delivery.act', command);

  if (reply === 'ok') {
    basket = {};
    openShop = null;
    note = '';
    tab = 'active';
    setTabs();
  } else if (reply === 'err:refused') {
    note = 'The order was refused - check the money and that a loading dock is free.';
  } else {
    note = 'That did not go through.';
  }

  refresh();
}

function renderDeliveries() {
  if (!deliveries.length) {
    bodyEl.appendChild(text('empty', tab === 'active' ? 'Nothing on the way.' : 'No past deliveries.'));
    return;
  }

  for (var i = 0; i < deliveries.length; i++) {
    var d = deliveries[i];

    var row = document.createElement('div');
    row.className = 'delivery';

    var head = document.createElement('div');
    head.className = 'delivery-head';
    head.appendChild(text('delivery-shop', d.shop));
    if (d.eta) head.appendChild(text('delivery-eta', d.eta));
    head.appendChild(text('delivery-status', d.status));
    row.appendChild(head);

    if (d.items && d.items.length) row.appendChild(text('delivery-items', d.items.join(', ')));

    bodyEl.appendChild(row);
  }
}

function findShop(id) {
  for (var i = 0; i < shops.shops.length; i++) if (shops.shops[i].id === id) return shops.shops[i];
  return null;
}

function findListing(shop, id) {
  for (var i = 0; i < shop.listings.length; i++) if (shop.listings[i].id === id) return shop.listings[i];
  return null;
}

function text(cls, value) {
  var el = document.createElement('div');
  el.className = cls;
  el.textContent = value;
  return el;
}

var tabs = document.querySelectorAll('.tab');
for (var t = 0; t < tabs.length; t++) tabs[t].addEventListener('click', pickTab);

function pickTab(e) {
  tab = e.currentTarget.getAttribute('data-tab');
  note = '';
  openShop = null;
  setTabs();
  refresh();
}

function setTabs() {
  var all = document.querySelectorAll('.tab');
  for (var i = 0; i < all.length; i++)
    all[i].className = 'tab' + (all[i].getAttribute('data-tab') === tab ? ' on' : '');
}

// Back steps out of an opened shop before it leaves the app - the same one-level-at-a-time behaviour the vanilla
// screens have.
document.addEventListener('back', function (e) {
  if (!openShop) return;

  e.preventDefault();
  openShop = null;
  render();
});

s1.on('reflash-delivery.changed', refresh);

refresh();
