// Journal. Rebuilt against the real screen - see Workspace/docs/Reflash/vanilla-dumps/vanilla-journal.txt.
//
// Vanilla shows details on HOVER. The renderer dispatches no mouseenter, so this selects on click instead: the
// same information, one press away. That is the one deliberate difference, and it is also what makes the app
// work on a touch screen, where hover does not exist either.
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
    row.className = 'quest' + (q.id === selected ? ' on' : '');
    row.setAttribute('data-id', q.id);

    // The little gold circle with a star that vanilla puts on every entry. A picture, not an asterisk: the game's
    // text atlases carry Latin-1 only, and a '*' sits too high to read as the badge vanilla draws.
    var icon = document.createElement('div');
    icon.className = 'quest-icon';

    var glyph = document.createElement('img');
    glyph.setAttribute('src', 'icon-star.png');
    icon.appendChild(glyph);
    row.appendChild(icon);

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

    row.addEventListener('click', pick);

    // Vanilla picks the task under the pointer. In the game there is no pointer and this never fires; in a browser
    // it does, and the app behaves exactly as the original.
    row.addEventListener('mouseover', pick);

    questsEl.appendChild(row);
  }
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
    // Vanilla says "Hover a task", because vanilla has a mouse. This renderer delivers no hover, so the sentence
    // names the gesture that actually works here rather than one the player would wait for in vain.
    hint.textContent = 'Select a task to view details';
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
  if (reply !== 'ok') btn.textContent = 'No spot';
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
