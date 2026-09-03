// DSLetras — editor entry point (Slice 1: carga de proyecto + canvas vacío)
import { EditorState } from './editor-state.js';
import { StatusHud } from './editor-status.js';

const state = new EditorState();
const hud = new StatusHud(state);
state.hud = hud;

await state.loadProject(document.getElementById('project-id').value);
hud.bind();
state.render();
state.bindUi();

export { state, hud };