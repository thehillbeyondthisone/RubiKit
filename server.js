const express = require('express');
const path = require('path');

const app = express();
const PORT = 5000;

app.use(express.static(path.join(__dirname, 'RubiKit')));

app.get('*', (req, res) => {
  res.sendFile(path.join(__dirname, 'RubiKit', 'index.html'));
});

app.listen(PORT, '0.0.0.0', () => {
  console.log(`RubiKit dashboard running on http://0.0.0.0:${PORT}`);
});
