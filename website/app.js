const previews = {
  home: ['assets/home.webp', 'Pancake Client desktop home screen'],
  mods: ['assets/mods.webp', 'Pancake Client Thunderstore mod browser'],
  obs: ['assets/obs.webp', 'Pancake Client Camera and OBS settings']
};
const image = document.getElementById('app-preview');
const buttons = document.querySelectorAll('[data-preview]');
buttons.forEach(button => button.addEventListener('click', () => {
  const preview = previews[button.dataset.preview];
  if (!preview || !image) return;
  image.src = preview[0];
  image.alt = preview[1];
  buttons.forEach(item => item.setAttribute('aria-pressed', String(item === button)));
}));
