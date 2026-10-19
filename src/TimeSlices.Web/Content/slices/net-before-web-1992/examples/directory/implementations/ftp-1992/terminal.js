// Средство визуализации, не код эпохи: пошаговый показ сеанса FTP (ftp-1992).
// Сеанс берётся из блока #transcript — дословной копии code/session.txt.
// Шаг заканчивается строкой, после которой пользователь вводит следующую команду;
// номера строк совпадают с полем lines стоп-кадров в process/ftp-session.json.
(function () {
  var lines = document.getElementById('transcript').textContent.replace(/^\n/, '').replace(/\n$/, '').split('\n');
  var staff = document.getElementById('staff').textContent.replace(/^\n/, '').replace(/\n$/, '');
  var stepEnds = [1, 2, 9, 12, 20, 28, 31, 32];   // последний шаг — вызов редактора, затем файл
  var screen = document.getElementById('screen');
  var step = 0;

  document.getElementById('total').textContent = stepEnds.length + 1;

  function show() {
    screen.textContent = '';
    if (step <= stepEnds.length) {
      if (step === 0) { screen.textContent = '% '; }
      else { lines.slice(0, stepEnds[step - 1]).forEach(showLine); }
    } else {
      // Файл в редакторе: поиск фамилии — забота пользователя, а не службы.
      staff.split('\n').forEach(function (line) {
        var span = document.createElement('span');
        span.textContent = line + '\n';
        if (line.indexOf('Иванов Иван') === 0) span.className = 'hit';
        if (line.charAt(0) === '/') span.className = 'typed';   // команда поиска в редакторе набрана пользователем
        screen.appendChild(span);
      });
    }
    document.getElementById('step').textContent = step;
    document.getElementById('next').disabled = step > stepEnds.length;
  }

  // Набранное пользователем выделяется: приглашение (%, ftp>, Name (...):) печатает программа,
  // остальное в такой строке набрано на клавиатуре.
  var promptRe = /^(% |ftp> |Name \([^)]*\): )(.*)$/;

  function showLine(line) {
    var m = promptRe.exec(line);
    if (!m) { screen.appendChild(document.createTextNode(line + '\n')); return; }
    screen.appendChild(document.createTextNode(m[1]));
    var typed = document.createElement('span');
    typed.className = 'typed';
    typed.textContent = m[2];
    screen.appendChild(typed);
    screen.appendChild(document.createTextNode('\n'));
  }

  function next() {
    if (step <= stepEnds.length) { step++; show(); }
  }

  document.getElementById('next').addEventListener('click', next);
  document.addEventListener('keydown', function (e) {
    if (e.key === 'Enter') { e.preventDefault(); next(); }
  });
  show();
}());
