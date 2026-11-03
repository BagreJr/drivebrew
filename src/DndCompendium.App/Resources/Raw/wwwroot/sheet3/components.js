(() => {
  'use strict';
  const api = globalThis.DrivebrewSheet3;
  const h = api.h = (tag, attributes = {}, ...children) => {
    const element = document.createElement(tag);
    for (const [key, value] of Object.entries(attributes || {})) {
      if (key === 'class') element.className = value;
      else if (key === 'text') element.textContent = value;
      else if (key.startsWith('on') && typeof value === 'function') element.addEventListener(key.slice(2).toLowerCase(), value);
      else if (key === 'checked') element.checked = Boolean(value);
      else if (key === 'value') element.value = value ?? '';
      else if (key === 'hidden') element.hidden = Boolean(value);
      else if (value !== undefined && value !== null) element.setAttribute(key, String(value));
    }
    for (const child of children.flat()) if (child != null) element.append(child.nodeType ? child : document.createTextNode(String(child)));
    return element;
  };
  api.sectionHeader = (title, description) => h('header',{class:'sectionHeader'},h('h2',{text:title}),h('p',{text:description}));
  api.card = (title, content, accent = false) => h('section',{class:`card${accent?' gold':''}`},
    title ? h('div',{class:'cardTitle'},h('h3',{text:title})) : null, content);
  api.empty = text => h('div',{class:'empty',text});
  api.button = (text, action, style = '') => h('button',{type:'button',class:`button ${style}`.trim(),text,onclick:action});
  api.field = (label, name, value, options = {}) => {
    const input = h(options.multiline?'textarea':options.select?'select':'input',{
      type:options.type||'text',inputmode:options.inputmode,placeholder:options.placeholder||'',value:value??''
    });
    if (options.select) for (const option of options.select) input.append(h('option',{value:option.value,text:option.label}));
    input.value = value ?? '';
    input.addEventListener(options.live?'input':'change', () => (options.onchange || (next => api.setField(name,next)))(input.value));
    return h('div',{class:`field${options.wide?' wide':''}`},h('label',{text:label}),input);
  };
  api.pill = (text, active = false, action = null) => h(action?'button':'span',{
    type:action?'button':null,class:`pill${active?' active':''}`,text,onclick:action
  });
  api.listItem = (title, detail, actionText, action) => h('article',{class:'listItem'},
    h('div',{},h('strong',{text:title}),detail?h('span',{text:detail}):null),
    actionText?api.button(actionText,action,'ghost'):null);
  api.modal = (title, body) => {
    const backdrop = h('div',{class:'modalBackdrop'});
    const panel = h('section',{class:'modal'},h('div',{class:'cardTitle'},h('h3',{text:title}),api.button('Cerrar',()=>backdrop.remove(),'ghost')),body);
    backdrop.addEventListener('click',event=>{if(event.target===backdrop)backdrop.remove();});backdrop.append(panel);document.body.append(backdrop);return backdrop;
  };
  api.readPortrait = file => new Promise((resolve,reject) => {
    if (!file || !file.type.startsWith('image/')) return reject(new Error('Elegí una imagen válida.'));
    const image = new Image(); const reader = new FileReader();
    reader.onerror=()=>reject(new Error('No se pudo leer la imagen.'));
    reader.onload=()=>{image.onload=()=>{const size=640;const scale=Math.min(1,size/Math.max(image.width,image.height));const canvas=document.createElement('canvas');canvas.width=Math.round(image.width*scale);canvas.height=Math.round(image.height*scale);canvas.getContext('2d').drawImage(image,0,0,canvas.width,canvas.height);resolve(canvas.toDataURL('image/jpeg',.82));};image.src=reader.result;};
    reader.readAsDataURL(file);
  });
})();
