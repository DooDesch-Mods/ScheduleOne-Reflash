// Connect. Shows the code, counts the devices, and carries the switch that starts the server.

// True when this page is being read on the paired phone rather than on the in-game one. The switch is hidden
// there: turning the server off from the device it is serving would cut the connection the press arrived on.
var RICH = typeof s1 !== 'undefined' && s1.rich === true;

var bodyEl = document.getElementById('body');
var devicesEl = document.getElementById('devices');

var state = { running: false, devices: 0, url: '', plain: '', qr: false, expires: 0, problem: '', takeover: false };

function read() {
  var raw = s1.call('reflash-connect.state', '');
  if (!raw) return;

  try { state = JSON.parse(raw); } catch (e) { s1.log('state did not parse: ' + e); }
}

function render() {
  devicesEl.textContent = state.devices ? state.devices + ' connected' : '';
  bodyEl.replaceChildren();

  if (!state.running) { renderOff(); return; }

  if (state.qr) {
    var img = document.createElement('img');
    img.className = 'qr';
    // Cache-busting is unnecessary: the mod replaces the picture under the same name and the renderer decodes it
    // again when it does.
    img.setAttribute('src', 's1://qr');
    img.setAttribute('width', '222');
    img.setAttribute('height', '222');
    bodyEl.appendChild(img);
  }

  bodyEl.appendChild(side());
}

function side() {
  var side = document.createElement('div');
  side.className = 'side';

  side.appendChild(step('1', 'Scan the code with your phone camera.'));
  side.appendChild(step('2', 'Your phone has to be on the same network as this PC.'));

  // Always show the address in text. A QR on a stream, behind sharpening or across a room is often unreadable,
  // and typing it in has to stay possible.
  var url = document.createElement('div');
  url.className = 'url';
  url.textContent = state.plain;
  side.appendChild(url);

  if (state.expires > 0) {
    var expiry = document.createElement('div');
    expiry.className = 'expiry';
    expiry.textContent = 'This code stops working in ' + state.expires + 's.';
    side.appendChild(expiry);
  } else {
    var stale = document.createElement('div');
    stale.className = 'expiry';
    stale.textContent = 'This code has expired.';
    side.appendChild(stale);
  }

  var buttons = document.createElement('div');
  buttons.className = 'buttons';
  buttons.appendChild(button('New code', 'new'));
  if (state.devices > 0) buttons.appendChild(button('Disconnect all', 'kick'));
  if (!RICH) buttons.appendChild(button('Turn off', 'off'));
  side.appendChild(buttons);

  var hint = document.createElement('div');
  hint.className = 'hint';
  hint.textContent = 'Nothing reaches this without the code. If your phone cannot connect, Windows Firewall is '
                   + 'the usual reason - allow the game on private networks.';
  side.appendChild(hint);

  if (!RICH) side.appendChild(takeover());

  return side;
}

// The staged rollout. These screens are built to replace the ones on this phone, and this is where someone who
// has been using them on a real phone can put them here too. Off to begin with, and honest about why.
function takeover() {
  var box = document.createElement('div');
  box.className = 'takeover';

  var text = document.createElement('div');
  text.className = 'takeover-text';
  text.textContent = state.takeover
    ? 'The icons on this phone open the Reflash apps.'
    : 'The icons on this phone still open the original apps.';
  box.appendChild(text);

  box.appendChild(button(state.takeover ? 'Use the original apps' : 'Use these apps here too',
                         state.takeover ? 'apps-off' : 'apps-on'));

  return box;
}

function step(n, text) {
  var row = document.createElement('div');
  row.className = 'step';

  var num = document.createElement('div');
  num.className = 'step-num';
  num.textContent = n;
  row.appendChild(num);

  var body = document.createElement('div');
  body.className = 'step-text';
  body.textContent = text;
  row.appendChild(body);

  return row;
}

function button(label, op) {
  var btn = document.createElement('button');
  btn.className = 'btn';
  btn.textContent = label;
  btn.addEventListener('click', function () {
    s1.call('reflash-connect.act', op);
    read();
    render();
  });
  return btn;
}

function renderOff() {
  var box = document.createElement('div');
  box.className = 'off';

  var title = document.createElement('div');
  title.className = 'off-title';
  title.textContent = 'Put this phone on your phone';
  box.appendChild(title);

  var text = document.createElement('div');
  text.className = 'off-text';
  text.textContent = 'Turn this on and scan the code, and the seven apps on this screen open on your real '
                   + 'phone - same save, same moment. It is off until you ask because it opens a port on '
                   + 'your local network. Nothing can use it without the code.';
  box.appendChild(text);

  if (state.problem) {
    var problem = document.createElement('div');
    problem.className = 'problem';
    problem.textContent = state.problem;
    box.appendChild(problem);
  }

  if (RICH) {
    // Reached by a device that is somehow still paired while the server is down - a stale page mid-shutdown.
    // There is no button to offer it: the switch lives on the machine running the game.
    var where = document.createElement('div');
    where.className = 'off-text';
    where.textContent = 'Turn it back on in the Connect app on the in-game phone.';
    box.appendChild(where);
  } else {
    var buttons = document.createElement('div');
    buttons.className = 'buttons';
    buttons.appendChild(button('Turn on', 'on'));
    box.appendChild(buttons);

    // Reachable without ever pairing a phone: someone may only want the replacements here.
    box.appendChild(takeover());
  }

  bodyEl.appendChild(box);
}

s1.on('reflash-connect.changed', function () {
  read();
  render();
});

read();
render();
