// Средство визуализации, не код эпохи: пошаговый показ сеанса FTP (ftp-1992).
// Сеанс берётся из блока #transcript — дословной копии code/session.txt.
// Шаг заканчивается строкой, после которой пользователь вводит следующую команду;
// номера строк совпадают с полем lines стоп-кадров в process/ftp-session.json.
(function () {
  var lines = document.getElementById('transcript').textContent.replace(/^\n/, '').replace(/\n$/, '').split('\n');
  var staff = document.getElementById('staff').textContent.replace(/^\n/, '').replace(/\n$/, '');
  var stepEnds = [3, 9, 12, 20, 28, 31, 32];   // последний шаг — вызов редактора, затем файл
  var screen = document.getElementById('screen');
  var step = 0;

  document.getElementById('total').textContent = stepEnds.length + 1;

  function show() {
    screen.textContent = '';
    if (step <= stepEnds.length) {
      screen.textContent = step === 0 ? '% ' : lines.slice(0, stepEnds[step - 1]).join('\n') + '\n';
    } else {
      // Файл в редакторе: поиск фамилии — забота пользователя, а не службы.
      staff.split('\n').forEach(function (line) {
        var span = document.createElement('span');
        span.textContent = line + '\n';
        if (line.indexOf('Иванов Иван') === 0) span.className = 'hit';
        screen.appendChild(span);
      });
    }
    document.getElementById('step').textContent = step;
    document.getElementById('next').disabled = step > stepEnds.length;
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
