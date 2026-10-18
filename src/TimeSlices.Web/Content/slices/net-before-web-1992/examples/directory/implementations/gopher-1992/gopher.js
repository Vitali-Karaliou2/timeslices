// Средство визуализации, не код эпохи: имитация текстового клиента Gopher (gopher-1992).
// Меню машины gopher.phys.example.edu берутся из блока #menus — дословной копии code/menu.txt:
// первый ответ (до строки ".") — главное меню (селектор — пустая строка), второй — меню /staff.
// Остальные ответы ниже — данные имитации, в код для показа не входят.
(function () {
  var HOME = 'gopher.phys.example.edu';
  var raw = document.getElementById('menus').textContent.split('\n.\n');
  var menus = {};
  menus[HOME + ' '] = raw[0];
  menus[HOME + ' /staff'] = raw[1];
  menus[HOME + ' /about'] = '0История кафедры\t/about/history.txt\t' + HOME + '\t70\n' +
                            '0Расписание семинаров\t/about/seminars.txt\t' + HOME + '\t70';
  menus['gopher.list.example.org /'] =
    '1Университет Миннесоты\t/\tgopher.umn.example.edu\t70\n' +
    '1Университет Иллинойса\t/\tgopher.uiuc.example.edu\t70\n' +
    '1Корнеллский университет\t/\tgopher.cornell.example.edu\t70';

  var docs = {};
  docs[HOME + ' /staff/staff.txt'] =
    'Кафедра физики. Список сотрудников.            Октябрь 1992.\n\n' +
    'Петров Пётр Сергеевич         заведующий         4410     301\n' +
    'Иванов Иван Иванович          профессор          4417     312\n' +
    'Сидорова Анна Михайловна      доцент             4421     315\n' +
    'Козлов Андрей Николаевич      доцент             4423     318\n' +
    'Морозова Елена Викторовна     ассистент          4425     320\n' +
    'Волков Дмитрий Олегович       инженер            4430     лаб. 104\n';
  docs[HOME + ' /about/history.txt'] = 'Кафедра физики основана в 1946 году.\n';
  docs[HOME + ' /about/seminars.txt'] = 'Семинар кафедры — по средам, 16:00, комната 301.\n';

  // Справочник CCSO (тип 2): отдельная служба на своей машине и своём порту.
  var ph = [
    ['Петров Пётр Сергеевич', 'заведующий кафедрой', '4410', '301'],
    ['Иванов Иван Иванович', 'профессор', '4417', '312'],
    ['Сидорова Анна Михайловна', 'доцент', '4421', '315'],
    ['Козлов Андрей Николаевич', 'доцент', '4423', '318'],
    ['Морозова Елена Викторовна', 'ассистент', '4425', '320'],
    ['Волков Дмитрий Олегович', 'инженер', '4430', 'лаб. 104']
  ];

  var screen = document.getElementById('screen');
  var wire = document.getElementById('wire');
  var input = document.getElementById('input');
  var label = document.getElementById('label');
  var stack = [];      // история — в клиенте; сервер её не знает
  var view = null;     // {kind: 'menu'|'doc'|'ph', title, host, selector, items}

  function parse(text) {
    return text.split('\n').filter(function (l) { return l.length > 0; }).map(function (l) {
      var f = l.split('\t');
      return { type: f[0].charAt(0), name: f[0].slice(1), selector: f[1], host: f[2], port: f[3] };
    });
  }

  function request(host, port, selector) {
    wire.textContent = 'Соединение с ' + host + ':' + port + ', запрос: ' +
      (selector === '' ? '(пустая строка)' : selector) + '\nОтвет получен, соединение закрыто.';
  }

  function suffix(type) { return type === '1' ? '/' : type === '2' ? ' <CSO>' : ''; }

  function render() {
    screen.textContent = '';
    var head = '        Internet Gopher Information Client\n\n        ' + view.title + '\n\n';
    screen.appendChild(document.createTextNode(head));
    if (view.kind === 'menu') {
      view.items.forEach(function (it, i) {
        var span = document.createElement('span');
        span.className = 'item';
        span.textContent = '     ' + (i + 1) + '.  ' + it.name + suffix(it.type) + '\n';
        span.addEventListener('click', function () { open(i); });
        screen.appendChild(span);
      });
      label.textContent = 'Номер пункта:';
    } else {
      screen.appendChild(document.createTextNode(view.body));
      label.textContent = view.kind === 'ph' ? 'Фамилия (name):' : 'Команда:';
    }
    input.value = '';
    input.focus();
  }

  function showMenu(title, host, port, selector) {
    request(host, port, selector);
    var text = menus[host + ' ' + selector];
    if (text === undefined) {
      view = { kind: 'doc', title: title, body: '(в имитации содержимое этой машины не показано)\n' };
    } else {
      view = { kind: 'menu', title: title, host: host, items: parse(text) };
    }
    render();
  }

  function open(i) {
    var it = view.items[i];
    if (!it) return;
    stack.push(view);
    if (it.type === '1') {
      showMenu(it.name, it.host, it.port, it.selector);
    } else if (it.type === '0') {
      request(it.host, it.port, it.selector);
      view = { kind: 'doc', title: it.name, body: docs[it.host + ' ' + it.selector] || '' };
      render();
    } else if (it.type === '2') {
      wire.textContent = 'Справочник CCSO на ' + it.host + ':' + it.port + ' — отдельная служба.';
      view = { kind: 'ph', title: it.name, host: it.host, port: it.port, body: 'Введите фамилию.\n' };
      render();
    }
  }

  function query(name) {
    var found = ph.filter(function (p) { return name && p[0].indexOf(name) === 0; });
    wire.textContent = 'Соединение с ' + view.host + ':' + view.port + ', запрос: query name=' + name +
      '\nОтвет получен, соединение закрыто.';
    view.body = found.length === 0 ? 'Ничего не найдено.\n' : found.map(function (p) {
      return '----------------------------------------\n' +
             '  name: ' + p[0] + '\n title: ' + p[1] + '\n phone: ' + p[2] + '\n  room: ' + p[3] + '\n';
    }).join('') + '----------------------------------------\n';
    render();
  }

  document.getElementById('bar').addEventListener('submit', function (e) {
    e.preventDefault();
    var v = input.value.trim();
    if (v === 'u' || v === 'U') {
      if (stack.length) { view = stack.pop(); wire.textContent = ''; render(); }
      return;
    }
    if (view.kind === 'menu') {
      var n = parseInt(v, 10);
      if (n >= 1 && n <= view.items.length) open(n - 1);
      else input.value = '';
    } else if (view.kind === 'ph') {
      query(v);
    }
  });

  showMenu('Кафедра физики', HOME, '70', '');
}());
