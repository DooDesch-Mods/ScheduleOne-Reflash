// Connect. Shows the code, counts the devices, and carries the switch that starts the server.

// True when this page is being read on the paired phone rather than on the in-game one. The switch is hidden
// there: turning the server off from the device it is serving would cut the connection the press arrived on.
var RICH = typeof s1 !== 'undefined' && s1.rich === true;

var bodyEl = document.getElementById('body');
var devicesEl = document.getElementById('devices');

var state = { running: false, devices: 0, url: '', plain: '', qr: false, expires: 0, problem: '', takeover: false, relay: false, requests: [], known: [] };

// 'main' or 'devices'. Page-local: the list of remembered devices is a second screen, the code stays the first.
var view = 'main';

function read() {
  var raw = s1.call('reflash-connect.state', '');
  if (!raw) return;

  try { state = JSON.parse(raw); } catch (e) { s1.log('state did not parse: ' + e); }
}

function render() {
  devicesEl.textContent = state.devices ? state.devices + ' connected' : '';
  bodyEl.replaceChildren();

  if (!state.running) { renderOff(); return; }

  // A phone asking to connect comes before everything else: it is waiting, and its request runs out.
  if (state.requests && state.requests.length) { renderRequests(); return; }

  if (view === 'devices' && state.known && state.known.length) { renderDevices(); return; }
  view = 'main';

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

  side.appendChild(step('1', 'Scan with your phone camera.'));
  side.appendChild(step('2', state.relay ? 'Works on any network.' : 'Phone and PC need the same network.'));

  // Always show the address in text. A QR on a stream, behind sharpening or across a room is often unreadable,
  // and typing it in has to stay possible.
  var url = document.createElement('div');
  url.className = 'url';
  url.textContent = state.plain;
  side.appendChild(url);

  if (state.expires > 0) {
    var expiry = document.createElement('div');
    expiry.className = 'expiry';
    expiry.textContent = 'Code expires in ' + state.expires + 's.';
    side.appendChild(expiry);
  } else {
    var stale = document.createElement('div');
    stale.className = 'expiry';
    stale.textContent = 'Code expired.';
    side.appendChild(stale);
  }

  var buttons = document.createElement('div');
  buttons.className = 'buttons';
  buttons.appendChild(button('New code', 'new'));
  if (state.known && state.known.length) buttons.appendChild(showDevices());
  if (state.devices > 0 || (state.known && state.known.length)) buttons.appendChild(button('Disconnect all', 'kick'));
  if (!RICH) buttons.appendChild(button('Turn off', 'off'));
  side.appendChild(buttons);

  var hint = document.createElement('div');
  hint.className = 'hint';
  hint.textContent = state.relay
    ? 'Encrypted, via relay.doodesch.de'
    : 'Won\'t connect? Allow the game on private networks in Windows Firewall.';
  side.appendChild(hint);

  // The relay switch. Hidden on the paired phone for the same reason as Turn off: it may be the connection the
  // press arrived on.
  if (!RICH) {
    var relay = document.createElement('div');
    relay.className = 'buttons';
    relay.appendChild(button(state.relay ? 'Same Wi-Fi only' : 'Allow any network', state.relay ? 'relay-off' : 'relay-on'));
    side.appendChild(relay);
  }

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
    ? 'Phone icons open Reflash.'
    : 'Phone icons open the original apps.';
  box.appendChild(text);

  box.appendChild(button(state.takeover ? 'Use original apps' : 'Use Reflash here too',
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

function button(label, op, cls) {
  var btn = document.createElement('button');
  btn.className = cls || 'btn';
  btn.textContent = label;
  btn.addEventListener('click', function () {
    var answer = s1.call('reflash-connect.act', op);
    if (answer !== 'ok') s1.log(op.split('\u001F')[0] + ' answered ' + answer);
    read();
    render();
  });
  return btn;
}

function renderRequests() {
  var box = document.createElement('div');
  box.className = 'requests';

  for (var i = 0; i < state.requests.length; i++) {
    var r = state.requests[i];

    var card = document.createElement('div');
    card.className = 'request';

    var title = document.createElement('div');
    title.className = 'request-title';
    title.textContent = r.name + ' wants to connect';
    card.appendChild(title);

    var text = document.createElement('div');
    text.className = 'request-text';
    text.textContent = RICH ? 'Answer it on the in-game phone.' : 'Remembered after you allow it.';
    card.appendChild(text);

    if (!RICH) {
      var buttons = document.createElement('div');
      buttons.className = 'buttons';
      buttons.appendChild(button('Allow', 'allow\u001F' + r.id, 'btn btn-allow'));
      buttons.appendChild(button('Deny', 'deny\u001F' + r.id));
      card.appendChild(buttons);
    }

    box.appendChild(card);
  }

  bodyEl.appendChild(box);
}

function showDevices() {
  var btn = document.createElement('button');
  btn.className = 'btn';
  btn.textContent = 'Devices (' + state.known.length + ')';
  btn.addEventListener('click', function () {
    view = 'devices';
    render();
  });
  return btn;
}

function renderDevices() {
  var box = document.createElement('div');
  box.className = 'known';

  var title = document.createElement('div');
  title.className = 'known-title';
  title.textContent = 'Remembered devices';
  box.appendChild(title);

  for (var i = 0; i < state.known.length; i++) {
    var d = state.known[i];

    var row = document.createElement('div');
    row.className = 'known-row';

    var name = document.createElement('div');
    name.className = 'known-name';
    name.textContent = d.name;
    row.appendChild(name);

    if (d.on) {
      var on = document.createElement('div');
      on.className = 'known-on';
      on.textContent = 'Connected';
      row.appendChild(on);
    }

    row.appendChild(button('Forget', 'forget\u001F' + d.id));
    box.appendChild(row);
  }

  var back = document.createElement('button');
  back.className = 'btn';
  back.textContent = 'Back';
  back.addEventListener('click', function () {
    view = 'main';
    render();
  });

  var buttons = document.createElement('div');
  buttons.className = 'buttons';
  buttons.appendChild(back);
  box.appendChild(buttons);

  bodyEl.appendChild(box);
}

function renderOff() {
  var box = document.createElement('div');
  box.className = 'off';

  var title = document.createElement('div');
  title.className = 'off-title';
  title.textContent = 'Open these apps on your phone';
  box.appendChild(title);

  var text = document.createElement('div');
  text.className = 'off-text';
  text.textContent = 'Opens a port on your local network. Only a device with the code can connect.';
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
    where.textContent = 'Turn it on in the Connect app on the in-game phone.';
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
