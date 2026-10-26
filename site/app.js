(() => {
  const primaryRepo = 'hawktuahs/capri-cyberpunk';
  const fallbackRepo = 'lachydotmcg/capri-desktop';
  const install = document.querySelector('#install');

  const enableDownload = (release) => {
    if (!release || release.draft || release.prerelease || !Array.isArray(release.assets)) return;
    const asset = release.assets.find(item => 
      (item.name.endsWith('.zip') || item.name.endsWith('.exe')) && 
      item.size > 1024 && 
      typeof item.browser_download_url === 'string'
    );
    if (asset && install) {
      install.href = asset.browser_download_url;
    }
  };

  const readRelease = async (url) => {
    const response = await fetch(url, { signal: AbortSignal.timeout(6000) });
    if (!response.ok) throw new Error('Release unavailable');
    enableDownload(await response.json());
  };

  readRelease('https://api.github.com/repos/' + primaryRepo + '/releases/latest')
    .catch(() => readRelease('https://api.github.com/repos/' + fallbackRepo + '/releases/latest'))
    .catch(() => readRelease('release.json'))
    .catch(() => {});

  const headsUp = document.querySelector('#heads-up');
  if (install && headsUp) {
    install.addEventListener('click', () => { if (headsUp.showModal && !headsUp.open) headsUp.showModal(); });
    headsUp.addEventListener('click', event => { if (event.target === headsUp) headsUp.close(); });
  }

  // AI Brains Tab Switching
  const tabBtns = document.querySelectorAll('.tab-btn');
  const tabContents = document.querySelectorAll('.tab-content');
  tabBtns.forEach(btn => {
    btn.addEventListener('click', () => {
      tabBtns.forEach(b => b.classList.remove('active'));
      tabContents.forEach(c => c.classList.remove('active'));
      btn.classList.add('active');
      const target = document.getElementById(btn.dataset.tab);
      if (target) target.classList.add('active');
    });
  });

  // Copy to clipboard buttons
  document.querySelectorAll('.copy-btn').forEach(btn => {
    btn.addEventListener('click', () => {
      const text = btn.dataset.copy;
      if (!text) return;
      navigator.clipboard.writeText(text).then(() => {
        const original = btn.textContent;
        btn.textContent = 'Copied!';
        setTimeout(() => { btn.textContent = original; }, 2000);
      }).catch(() => {});
    });
  });

  // Interactive Stage & Capri Simulation
  const stage = document.querySelector('#demo-box') || document.querySelector('#demo');
  const capri = document.querySelector('#demo-capri');
  if (!stage || !capri) return;

  const sprite = capri.querySelector('.sprite');
  const cursor = document.querySelector('#player-cursor');
  const status = document.querySelector('#demo-status');
  const motion = !matchMedia('(prefers-reduced-motion: reduce)').matches;
  const clips = { 
    idle: [16, 8], blink: [4, 8], blink_glitch: [4, 8], grin: [16, 8], glitch: [10, 15], 
    dangle: [24, 10], sleep: [16, 6], wake: [13, 15], perch: [16, 8], perch_blink: [4, 8], 
    perch_blink_glitch: [4, 8], perch_glitch: [10, 15], mouth: [5, 12] 
  };
  const loaded = new Set(['idle']);
  let clip = 'idle';
  let clipStart = performance.now();
  let cycle = 0;
  let blinkWith = '';
  let nextGlitch = clipStart + 12000 + Math.random() * 12000;
  let lastInput = clipStart;
  let shown = '';
  let layered = '';
  let currentAnswer = "I can't reply through here choom, you needa install my chip above!";

  const play = name => { 
    clip = loaded.has(name) ? name : riding && loaded.has('perch') ? 'perch' : 'idle'; 
    clipStart = performance.now(); 
    cycle = 0; 
    blinkWith = ''; 
  };

  const show = (name, cell, mouth) => {
    const layers = mouth >= 0 ? ['mouth', name] : [name];
    if (layered !== layers.join()) {
      layered = layers.join();
      sprite.style.backgroundImage = layers.map(layer => 'url("assets/sprites/' + layer + '.webp")').join(', ');
      sprite.style.backgroundSize = layers.map(layer => clips[layer][0] * 100 + '% 100%').join(', ');
    }
    if (shown === name + ':' + cell + ':' + mouth) return;
    shown = name + ':' + cell + ':' + mouth;
    sprite.style.backgroundPosition = (mouth >= 0 ? mouth / 4 * 100 + '% 0, ' : '') + cell / (clips[name][0] - 1) * 100 + '% 0';
  };

  const mouthFor = (letter, step) => {
    const lower = letter.toLowerCase();
    if ('aeo'.includes(lower)) return letter !== lower ? 4 : 3;
    if ('iuy'.includes(lower)) return 2;
    if ('mbp'.includes(lower) || !/\p{L}/u.test(letter)) return 0;
    return step % 2 ? 1 : 2;
  };

  const animate = now => {
    const seated = clip.startsWith('perch');
    if (riding && !seated && loaded.has('perch')) play('perch');
    if ((!riding && seated) || (clip === 'dangle' && !(drag && drag.moved))) play('idle');
    let index = Math.max(0, Math.floor((now - clipStart) / 1000 * clips[clip][1]));
    if ((clip === 'glitch' && index >= clips.glitch[0]) || (clip === 'wake' && index >= clips.wake[0]) || (clip === 'grin' && index >= clips.grin[0] * 2)) { play('idle'); index = 0; }
    if (clip === 'perch_glitch' && index >= clips.perch_glitch[0]) { play('perch'); index = 0; }
    if ((clip === 'idle' || clip === 'perch') && motion) {
      const current = Math.floor(index / clips[clip][0]);
      if (current !== cycle) {
        cycle = current;
        const roll = Math.random();
        const prefix = clip === 'perch' ? 'perch_' : '';
        blinkWith = roll < 0.1 ? prefix + 'blink_glitch' : roll < 0.55 ? prefix + 'blink' : '';
        if (now >= nextGlitch && talkState === 'off') { nextGlitch = now + 12000 + Math.random() * 12000; play(clip === 'perch' ? 'perch_glitch' : 'glitch'); index = 0; }
      }
      if (clip === 'idle' && now - lastInput > 30000 && !riding && !drag && talkState === 'off' && typer.hidden) play('sleep');
    }
    let name = clip;
    let cell = motion ? index % clips[clip][0] : 0;
    if ((name === 'idle' || name === 'perch') && blinkWith && loaded.has(blinkWith) && cell >= 9 && cell < 13) { name = blinkWith; cell -= 9; }
    capri.classList.toggle('still', clip === 'sleep' || clip === 'dangle');
    let mouth = -1;
    if (talkState === 'talking' && motion && clip === 'idle' && name !== 'blink_glitch' && loaded.has('mouth')) {
      const step = Math.floor((now - replyStart) / 1000 * 12);
      const letter = Math.floor(step / 12 * 28);
      if (letter < currentAnswer.length) mouth = mouthFor(currentAnswer[letter], step);
    }
    show(name, cell, mouth);
    updateTalk(now);
    requestAnimationFrame(animate);
  };

  const wake = () => { lastInput = performance.now(); if (clip === 'sleep') play('wake'); };
  ['pointermove', 'pointerdown', 'keydown', 'wheel'].forEach(type => document.addEventListener(type, wake, { passive: true }));
  
  addEventListener('load', () => Object.keys(clips).forEach(name => {
    const strip = new Image();
    strip.src = 'assets/sprites/' + name + '.webp';
    strip.decode().then(() => loaded.add(name)).catch(() => {});
  }));
  requestAnimationFrame(animate);

  let x = 0;
  let y = 0;
  let size = 220;
  let drag = null;
  let riding = false;
  let clickTimer;
  const taskbar = document.querySelector('#taskbar');
  const floor = () => stage.clientHeight - (taskbar ? taskbar.offsetHeight : 30);
  const cancelClick = () => clearTimeout(clickTimer);
  const setRide = on => { 
    cancelClick(); 
    const was = riding; 
    riding = on; 
    if (on && !was) play('perch_glitch'); 
    if (was && !on) play('grin'); 
    capri.classList.toggle('riding', on); 
    if (on) stage.focus({ preventScroll: true }); 
  };

  const placeCursor = (left, top) => {
    if (!cursor) return;
    left = Math.max(0, Math.min(left, stage.clientWidth - 26));
    top = Math.max(0, Math.min(top, stage.clientHeight - 32));
    cursor.style.transform = 'translate(' + left + 'px, ' + top + 'px)';
  };

  const followPointer = (event) => {
    const bounds = stage.getBoundingClientRect();
    placeCursor(event.clientX - bounds.left, event.clientY - bounds.top);
    if (riding) { 
      x = event.clientX - bounds.left - size * 0.267 + 12; 
      y = event.clientY - bounds.top - size * 0.73 - 12; 
      place(); 
    }
  };

  const place = () => {
    x = Math.max(0, Math.min(x, stage.clientWidth - size));
    y = Math.max(0, Math.min(y, floor() - size));
    capri.style.left = x + 'px';
    capri.style.top = y + 'px';
    capri.style.width = size + 'px';
    capri.style.height = size + 'px';
  };

  const reset = () => {
    size = stage.clientWidth < 550 ? 160 : 220;
    x = stage.clientWidth * (stage.clientWidth < 550 ? 0.46 : 0.62);
    y = floor() * 0.3;
    closeTalk();
    closeTyper();
    setRide(false);
    capri.hidden = false;
    play('grin');
    place();
    placeCursor(x + size * 0.6, y + size * 0.5);
    if (status) status.textContent = 'Capri is back.';
  };

  const endDrag = () => { 
    drag = null; 
    capri.classList.remove('dragging'); 
    if (cursor) cursor.classList.remove('pressed'); 
  };

  stage.addEventListener('pointermove', followPointer);
  stage.addEventListener('pointerdown', event => { 
    followPointer(event); 
    if (cursor) cursor.classList.add('pressed'); 
  });
  document.addEventListener('pointerup', () => { if (cursor) cursor.classList.remove('pressed'); });
  stage.addEventListener('pointercancel', endDrag);

  capri.addEventListener('pointerdown', event => {
    if (event.button !== 0) return;
    drag = { id: event.pointerId, startX: event.clientX, startY: event.clientY, originX: x, originY: y, moved: false };
    capri.setPointerCapture(event.pointerId);
  });

  capri.addEventListener('pointermove', event => {
    if (!drag || drag.id !== event.pointerId) return;
    const dx = event.clientX - drag.startX;
    const dy = event.clientY - drag.startY;
    if (dx * dx + dy * dy > 25 && !drag.moved) { drag.moved = true; cancelClick(); play('dangle'); }
    if (!drag.moved) return;
    capri.classList.add('dragging');
    x = drag.originX + dx;
    y = drag.originY + dy;
    place();
  });

  capri.addEventListener('pointerup', event => {
    if (!drag || drag.id !== event.pointerId) return;
    const wasMoved = drag.moved;
    endDrag();
    try { capri.releasePointerCapture(event.pointerId); } catch {}
    if (wasMoved) {
      if (y + size > floor() - 20) {
        y = floor() - size;
        play('perch');
        place();
      } else {
        play('idle');
      }
    } else {
      play(clip === 'grin' ? 'glitch' : 'grin');
    }
  });

  const restoreBtn = document.querySelector('#demo-restore');
  if (restoreBtn) restoreBtn.addEventListener('click', reset);

  // TALK BOX & SCANNER FRAME
  const pill = document.querySelector('#talk-pill');
  const box = document.querySelector('#talk-box');
  const typer = document.querySelector('#talk-typer');
  const input = document.querySelector('#talk-input');
  const heardLine = box ? box.querySelector('.talk-heard') : null;
  const words = box ? box.querySelector('.talk-words') : null;

  let talkState = 'off';
  let thinkUntil = 0;
  let replyStart = 0;
  let replyDone = 0;
  let pillUntil = 0;
  let leaving = 0;

  const clamp = (v, min, max) => Math.max(min, Math.min(v, max));
  const head = () => ({ x: x + size * 0.5, y: y + size * 0.15 });
  const feet = () => ({ x: x + size * 0.5, y: y + size * 0.85 });
  const underPill = () => ({ x: feet().x, y: feet().y + 42 });
  let boxBelow = false;

  const plate = (left, top, width, height, radius, stemX, stemY, stemWidth, open) => {
    return `M ${left + radius} ${top} H ${left + width - radius} Q ${left + width} ${top} ${left + width} ${top + radius} V ${top + height - radius} Q ${left + width} ${top + height} ${left + width - radius} ${top + height} H ${left + radius} Q ${left} ${top + height} ${left} ${top + height - radius} V ${top + radius} Q ${left} ${top} ${left + radius} ${top} Z`;
  };

  const frame = (svg, id, width, height, pathData) => {
    if (!svg) return;
    svg.setAttribute('viewBox', `0 0 ${width} ${height}`);
    svg.setAttribute('width', width);
    svg.setAttribute('height', height);
    svg.innerHTML = `<path d="${pathData}" fill="rgba(14, 18, 26, 0.94)" stroke="#38bdf8" stroke-width="1.2"/>`;
  };

  const placeBox = () => {
    if (!box) return;
    const width = box.offsetWidth;
    const height = box.offsetHeight;
    const tail = 16;
    const low = floor() - 4;
    const above = head();
    const under = underPill();
    boxBelow = above.y - tail - height < 4 && (under.y + tail + height <= low || low - under.y > above.y);
    const tip = boxBelow ? under : above;
    const left = clamp(tip.x - width * 0.7, 4, stage.clientWidth - width - 4);
    const top = clamp(boxBelow ? tip.y + tail : tip.y - tail - height, 4, low - height);
    box.style.left = left + 'px';
    box.style.top = top + 'px';
    frame(box.querySelector('svg'), 'talk-box-frame', width, height, plate(0, 0, width, height, 10, tip.x - left, boxBelow ? -tail : height + tail, 18, false));
  };

  const placeTyper = () => {
    if (!typer) return;
    const width = typer.offsetWidth;
    const height = typer.offsetHeight;
    const low = floor() - 4;
    let left = clamp(head().x - width * 0.7, 4, stage.clientWidth - width - 4);
    let top = clamp(head().y - height - 18, 4, low - height);
    typer.style.left = left + 'px';
    typer.style.top = top + 'px';
    frame(typer.querySelector('svg'), 'talk-typer-frame', width, height, plate(1, 1, width - 2, height - 2, 8, -1, height / 2, 0, true));
  };

  const placePill = () => {
    if (!pill) return;
    const width = pill.offsetWidth;
    const height = pill.offsetHeight;
    pill.style.left = clamp(feet().x - 4 - width / 2, 4, stage.clientWidth - width - 4) + 'px';
    pill.style.top = clamp(feet().y + 4, 4, stage.clientHeight - height - 2) + 'px';
  };

  const closeTalk = () => { talkState = 'off'; leaving = 0; if (box) { box.hidden = true; box.classList.remove('glitch', 'leaving'); } };
  const closeTyper = () => { if (!typer || typer.hidden) return; typer.hidden = true; if (talkState === 'online') replyDone = performance.now(); };
  const dismiss = () => { if (talkState !== 'off' && !leaving && box) { leaving = performance.now(); box.classList.add('leaving'); } };

  const getSmartReply = (question) => {
    const q = question.toLowerCase();
    if (q.includes('who') || q.includes('creator') || q.includes('created')) {
      return "Got compiled in Night City, choom. Inspired by Edgerunners Season 2, built to run completely on your local PC!";
    }
    if (q.includes('screen') || q.includes('look') || q.includes('inspect')) {
      return "Once you download my standalone exe, my RapidOCR vision inspects your active windows in real-time!";
    }
    if (q.includes('voice') || q.includes('mommy') || q.includes('asmr')) {
      return "Run setup_voice.bat to get my Kokoro-82M neural voices: Echo Construct and Mommy ASMR! Toggle anytime in Cyberdeck.";
    }
    if (q.includes('time') || q.includes('night city') || q.includes('clock')) {
      return `Current local net-time is ${new Date().toLocaleTimeString()}. Always watchful over your desktop, choom.`;
    }
    if (q.includes('takeover') || q.includes('hack')) {
      return "Every 10 mins I can take over your full display with a holographic cut-in! You can toggle it off in the Remote.";
    }
    return "I can only give preview quips in browser, choom! Download my v1.2.0 ZIP above to hear my real Kokoro neural voice!";
  };

  const hear = text => {
    text = text.trim().slice(0, 240);
    if (!text || !box) return;
    lastInput = performance.now();
    currentAnswer = getSmartReply(text);
    if (heardLine) heardLine.textContent = '> you: ' + text;
    if (words) words.textContent = '';
    talkState = 'thinking';
    thinkUntil = performance.now() + 600;
    leaving = 0;
    box.classList.remove('leaving');
    box.hidden = false;
    if (motion) { box.classList.remove('glitch'); void box.offsetWidth; box.classList.add('glitch'); }
    box.style.minHeight = '';
    if (words) words.textContent = currentAnswer;
    box.style.minHeight = box.offsetHeight + 'px';
    if (words) words.textContent = '';
  };

  const openTyper = () => {
    if (!typer) return;
    if (!typer.hidden) { closeTyper(); return; }
    typer.hidden = false;
    placeTyper();
    if (input) input.focus({ preventScroll: true });
    pillUntil = performance.now() + 1500;
  };

  const updateTalk = now => {
    const free = !capri.hidden && !riding && !(drag && drag.moved);
    const showPill = free && (now < pillUntil || (typer && !typer.hidden) || talkState !== 'off' || (pill && pill.contains(document.activeElement)));
    if (pill) {
      if (pill.hidden === showPill) pill.hidden = !showPill;
      if (showPill) placePill();
    }
    if (talkState === 'thinking' && now >= thinkUntil) { 
      talkState = 'talking'; 
      replyStart = now; 
      if (status) status.textContent = 'Capri says: ' + currentAnswer; 
    }
    if (talkState === 'talking' && (!motion || (now - replyStart) / 1000 * 28 >= currentAnswer.length)) { 
      talkState = 'online'; 
      replyDone = now; 
    }
    if (talkState === 'online' && typer && typer.hidden && now > replyDone + Math.min(14, 4 + currentAnswer.length * 0.05) * 1000) {
      dismiss();
    }
    if (leaving && now - leaving > 250) closeTalk();
    if (box && !box.hidden) {
      const shownWords = talkState === 'thinking' ? '' : talkState === 'online' ? currentAnswer : currentAnswer.slice(0, Math.floor((now - replyStart) / 1000 * 28));
      if (words && words.textContent !== shownWords) words.textContent = shownWords;
      ['thinking', 'talking', 'online'].forEach(state => box.classList.toggle(state, talkState === state));
      placeBox();
    }
    if (typer && !typer.hidden) placeTyper();
  };

  if (box) {
    box.addEventListener('animationend', () => box.classList.remove('glitch'));
    box.addEventListener('click', dismiss);
  }
  if (typer && input) {
    typer.addEventListener('submit', event => { 
      event.preventDefault(); 
      const text = input.value; 
      input.value = ''; 
      hear(text); 
    });
    input.addEventListener('keydown', event => { 
      if (event.key === 'Escape') { 
        event.preventDefault(); 
        closeTyper(); 
        capri.focus({ preventScroll: true }); 
      } 
    });
  }

  const typeBtn = document.querySelector('#talk-type');
  if (typeBtn) typeBtn.addEventListener('click', openTyper);
  const voiceBtn = document.querySelector('#talk-voice');
  if (voiceBtn) voiceBtn.addEventListener('click', () => hear('Hey Capri!'));

  // PROMPT CHIPS
  document.querySelectorAll('.chip-btn').forEach(btn => {
    btn.addEventListener('click', () => {
      const say = btn.dataset.say;
      if (say) hear(say);
    });
  });

  // KEYBOARD UNSEAT SHORTCUT: Ctrl + Alt + C
  document.addEventListener('keydown', event => {
    if (event.ctrlKey && event.altKey && (event.key === 'c' || event.key === 'C')) {
      reset();
    }
  });

  // GITHUB CHANGELOG
  const changelog = document.querySelector('#changelog');
  const track = changelog ? changelog.querySelector('.changelog-track') : null;
  const day = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short' });

  const readChanges = async (repoName) => {
    const response = await fetch('https://api.github.com/repos/' + repoName + '/commits?per_page=15', { signal: AbortSignal.timeout(6000) });
    if (!response.ok) throw new Error('Changes unavailable');
    const commits = await response.json();
    if (!Array.isArray(commits)) throw new Error('Invalid format');
    const rows = commits.filter(item => item && item.commit && !item.commit.message.startsWith('Merge ')).map(item => {
      const row = document.createElement('li');
      const link = document.createElement('a');
      link.href = item.html_url;
      link.target = '_blank';
      link.rel = 'noopener noreferrer';
      const when = document.createElement('time');
      when.dateTime = item.commit.author ? item.commit.author.date : '';
      when.textContent = item.commit.author ? day.format(new Date(item.commit.author.date)) : 'Recent';
      const text = document.createElement('span');
      text.className = 'changelog-text';
      text.textContent = item.commit.message.split('\n')[0];
      link.append(when, text);
      row.append(link);
      return row;
    });
    if (!rows.length) throw new Error('No changes');
    if (track) track.replaceChildren(...rows);
  };

  if (changelog && track) {
    readChanges(primaryRepo)
      .catch(() => readChanges(fallbackRepo))
      .catch(() => {});
  }

  // STAR BUTTON
  const star = document.querySelector('#star');
  if (star) {
    const starCount = star.querySelector('span');
    let stars = 42;
    star.addEventListener('click', () => {
      stars += 1;
      if (starCount) starCount.textContent = ' ' + stars;
      star.style.transform = 'scale(1.1)';
      setTimeout(() => { star.style.transform = 'none'; }, 200);
    });
  }

  // INITIALIZE POSITION
  reset();
})();
