// Journal. Rebuilt against the real screen - see Workspace/docs/Reflash/vanilla-dumps-047/vanilla-journal.txt.
//
// Vanilla shows details on HOVER, and so does this page. A click picks as well, which is what makes the app work
// on a touch screen, where hover does not exist.
//
// Three rules every Reflash page follows:
//   * s1.call is synchronous - never `await`, that deadlocks the game.
//   * a render replaces the DOM, so no element reference survives it.
//   * ASCII only. The font atlases carry Latin-1; an ellipsis or an arrow draws as an empty box.

// Field separator for a command: op, then fields, joined with U+001F. A unit separator rather than a comma or a
// newline because a task title may contain either, and a command splitting in half is not a failure anyone sees.
var SEP = String.fromCharCode(31);

var appEl = document.getElementById('app');
var questsEl = document.getElementById('quests');
var emptyEl = document.getElementById('empty');
var detailEl = document.getElementById('detail');
var titleEl = document.getElementById('topbar-title');

var state = { quests: [], rank: null };
var selected = null;

function read() {
  var raw = s1.call('reflash-journal.state', '');
  if (!raw) { state = { quests: [], rank: null }; return; }

  try {
    state = JSON.parse(raw);
  } catch (e) {
    // A failed handler returns "" and a broken one returns something unparseable. Neither should take the page
    // down - an empty task list reads as "nothing to do", which is at least honest.
    s1.log('state did not parse: ' + e);
    state = { quests: [], rank: null };
  }

  if (!state.quests) state.quests = [];
}

function current() {
  for (var i = 0; i < state.quests.length; i++)
    if (state.quests[i].id === selected) return state.quests[i];
  return null;
}

function render() {
  // Vanilla titles this app "Journal" and shows the rank nowhere on it, so neither does this.
  titleEl.textContent = 'Journal';

  renderList();
  renderDetail();
}

function renderList() {
  questsEl.replaceChildren();

  // The note sits inside the scroll box - replaceChildren just removed it, so put it back.
  emptyEl.className = state.quests.length ? 'empty' : 'empty on';
  questsEl.appendChild(emptyEl);

  for (var i = 0; i < state.quests.length; i++) {
    var q = state.quests[i];

    var row = document.createElement('div');
    row.className = 'quest' + (q.tracked ? ' tracked' : '') + (q.id === selected ? ' on' : '');
    row.setAttribute('data-id', q.id);

    // Vanilla rings the badge of a tracked task with a white disc. The ring is also the switch on a touch screen,
    // where a tap on the row has to open the details instead.
    var mark = document.createElement('div');
    mark.className = 'quest-mark';
    mark.setAttribute('data-id', q.id);
    mark.addEventListener('click', track);

    // The little gold circle with a star that vanilla puts on every entry. A picture, not an asterisk: the game's
    // text atlases carry Latin-1 only, and a '*' sits too high to read as the badge vanilla draws.
    var icon = document.createElement('div');
    icon.className = 'quest-icon';

    var glyph = document.createElement('img');
    glyph.setAttribute('src', 'icon-star.png');
    icon.appendChild(glyph);
    mark.appendChild(icon);
    row.appendChild(mark);

    var title = document.createElement('div');
    title.className = 'quest-title';
    title.textContent = q.title;
    row.appendChild(title);

    if (q.expires) {
      var timer = document.createElement('div');
      timer.className = 'timer' + (q.critical ? ' critical' : '');
      timer.textContent = q.expires;
      row.appendChild(timer);
    }

    row.addEventListener('click', press);

    // Vanilla picks the task under the pointer, and so does this, in the game and in a browser.
    row.addEventListener('mouseover', pick);

    questsEl.appendChild(row);
  }
}

// When the last touch landed. A tap also fires mouseover and click, so this is how a click tells a finger from a
// mouse - the game has no touch events, so there it stays 0.
var touchedAt = 0;
document.addEventListener('touchstart', function () { touchedAt = Date.now(); });

function press(e) {
  pick(e);

  // A vanilla click on a row tracks or untracks it; the details already came up on hover. A finger has no hover,
  // so its tap only opens the details and the ring does the tracking.
  if (Date.now() - touchedAt > 1000) toggle(e.currentTarget.getAttribute('data-id'));
}

function track(e) {
  e.stopPropagation();
  toggle(e.currentTarget.getAttribute('data-id'));
}

function toggle(id) {
  var reply = s1.call('reflash-journal.act', 'track' + SEP + id);
  if (reply !== 'ok') s1.log('track ' + id + ' refused: ' + reply);

  read();
  render();
}

function pick(e) {
  selected = e.currentTarget.getAttribute('data-id');
  appEl.classList.add('show-detail');
  render();
}

function renderDetail() {
  detailEl.replaceChildren();

  var q = current();
  if (!q) {
    var hint = document.createElement('div');
    hint.className = 'hint on';
    // Vanilla's sentence in the game, where a hover picks the task; a phone has no hover, so it is told to tap.
    hint.appendChild(span('mouse', 'Hover a task to view details'));
    hint.appendChild(span('touch', 'Tap a task to view details'));
    detailEl.appendChild(hint);
    return;
  }

  var title = document.createElement('div');
  title.className = 'detail-title';
  title.textContent = q.title;
  detailEl.appendChild(title);

  if (q.description) {
    var desc = document.createElement('div');
    desc.className = 'detail-desc';
    desc.textContent = q.description;
    detailEl.appendChild(desc);
  }

  if (q.expires) {
    var expiry = document.createElement('div');
    expiry.className = 'detail-label' + (q.critical ? ' critical' : '');
    expiry.textContent = 'Expires in ' + q.expires;
    detailEl.appendChild(expiry);
  }

  var steps = q.steps || [];
  if (!steps.length) return;

  var label = document.createElement('div');
  label.className = 'detail-label';
  label.textContent = 'Tasks';
  detailEl.appendChild(label);

  for (var i = 0; i < steps.length; i++) detailEl.appendChild(stepRow(q, steps[i], i));

  var end = document.createElement('div');
  end.className = 'steps-end';
  detailEl.appendChild(end);
}

function span(cls, text) {
  var el = document.createElement('div');
  el.className = cls;
  el.textContent = text;
  return el;
}

function stepRow(quest, step, index) {
  var row = document.createElement('div');
  row.className = 'step' + (step.state === 'completed' ? ' done' : step.state === 'failed' ? ' failed' : '');

  var title = document.createElement('div');
  title.className = 'step-title';
  title.textContent = step.title;
  row.appendChild(title);

  // Vanilla writes the state out at the right edge - "Completed", "Active" - rather than drawing a tick.
  var mark = document.createElement('div');
  mark.className = 'step-state';
  mark.textContent = step.state === 'completed' ? 'Completed' : step.state === 'failed' ? 'Failed' : 'Active';
  row.appendChild(mark);

  // Only an active step with somewhere to point at gets the button - offering it otherwise would be a button
  // that always refuses.
  if (step.poi && step.state === 'active') {
    var pin = document.createElement('button');
    pin.className = 'pin';
    pin.textContent = 'Map';
    pin.setAttribute('data-quest', quest.id);
    pin.setAttribute('data-step', String(index));
    pin.addEventListener('click', showOnMap);
    row.appendChild(pin);
  }

  return row;
}

function showOnMap(e) {
  var btn = e.currentTarget;
  e.stopPropagation();

  var reply = s1.call('reflash-journal.act',
                      'map' + SEP + btn.getAttribute('data-quest') + SEP + btn.getAttribute('data-step'));

  // Opening the map closes this app, so there is nothing to update on success. A refusal is worth saying out
  // loud rather than looking like a dead button.
  if (reply !== 'ok') btn.textContent = 'Not on map';
}

document.getElementById('back').addEventListener('click', toList);

document.addEventListener('back', function (e) {
  if (s1.orientation !== 'portrait') return;          // landscape shows both panes; there is nowhere to go back to
  if (!appEl.classList.contains('show-detail')) return;

  e.preventDefault();
  toList();
});

function toList() {
  appEl.classList.remove('show-detail');
  render();
}

// The mod pushes only a revision number; the page decides what to re-read. An idle app therefore costs one
// integer comparison per tick on the mod side and nothing at all here.
s1.on('reflash-journal.changed', function () {
  read();
  render();
});

read();
render();
