(() => {
  'use strict';
  const api=globalThis.DrivebrewSheet3,h=(...args)=>api.h(...args),root=document.getElementById('app');
  function header(){
    const values=api.calculated(),name=values.CharacterName||api.state.payload.characterName||'Personaje';
    return h('header',{class:'topbar referenceTopbar'},
      h('button',{type:'button',class:'topIcon','aria-label':'Volver',text:'‹',onclick:()=>api.send({type:'closeSheet'})}),
      h('div',{class:'topIdentity'},h('strong',{text:name}),h('span',{text:values['Spellcasting Class 2']?'CON MAGIA':'HOJA DE PERSONAJE'})),
      h('span',{class:`saveState${api.state.saving?' saving':''}`,text:api.state.saving?'Guardando…':'Guardado'}),
      h('button',{type:'button',class:'topIcon','aria-label':'Abrir PDF',text:'▣',onclick:()=>api.send({type:'openPdf'})}),
      h('button',{type:'button',class:'topIcon','aria-label':'Exportar PDF',text:'⇩',onclick:()=>api.send({type:'sharePdf'})}),
      h('button',{type:'button',class:'topIcon','aria-label':'Cambiar tema',text:'◐',onclick:()=>api.toggleSheetTheme?.()}),
      h('button',{type:'button',class:'topIcon toolIcon','aria-label':'Herramientas',text:'⬡',onclick:()=>api.openSheetTools?.()}));
  }
  function render(){if(!api.state.loaded)return;root.replaceChildren();if(api.state.payload.isCreationDraft){root.append(api.renderCreator());return;}root.append(header(),api.renderNav(),api.renderCharacter());}
  api.subscribe(render);
  const preview = new URLSearchParams(globalThis.location.search);
  if (preview.has('preview')) {
    api.state.section = preview.get('section') || 'front';
    api.receive({type:'load',payload:{
      characterName:'Nyx Veyra',isCreationDraft:preview.get('mode')==='creator',creatorStep:Number(preview.get('step')||1),
      portraitDataUrl:null,textFields:{CharacterName:'Nyx Veyra','Race ':'Aasimar',ClassLevel:'Psion 7','Digital.Subclass':'Awakened Mind',Background:'Sage',HPCurrent:'43',HPMax:'52',HPTemp:'6',AC:'17',Speed:'30 ft',HD:'5d8',HDTotal:'7d8',Inspiration:'1',STR:'8',DEX:'16',CON:'14',INT:'19',WIS:'13',CHA:'12','SpellcastingAbility 2':'INT',Backstory:'Nyx escucha pensamientos como ecos en una biblioteca sin puertas.','PersonalityTraits ':'Observadora y paciente.','Digital.Notes':'Juró encontrar el origen de la voz que la acompaña.'},
      checkedFields:[],abilityBaseScores:{STR:8,DEX:16,CON:14,INT:19,WIS:13,CHA:12},proficiencyRanks:{Arcana:2,'Investigation ':1,'Perception ':1},otherBonuses:{},overrides:{},modifiers:[],
      selections:[{Id:'class-psion',Kind:'Class',Name:'Psion',IsActive:true}],effects:[],features:[{Id:'telepathy',Name:'Telepathic Speech',Kind:'ClassFeature',Level:'1'},{Id:'mind-eye',Name:'Mind\'s Eye',Kind:'SubclassFeature',Level:'3'}],classLevels:[{Id:'psion',Name:'Psion',Level:7,IsPrimary:true,SubclassName:'Awakened Mind'}],
      conditions:[{Id:'focused',Name:'Focused',IsActive:true}],attacks:[{Id:'blade',Name:'Mind Blade',AttackBonus:'+7',Damage:'1d8+4 psychic',IsEquipped:true}],inventory:[{Id:'focus',Name:'Crystal focus',Kind:'Equipment',Quantity:1}],
      preparedSpells:[{id:'detect-thoughts',name:'Detect Thoughts',level:2,school:'Divination',castingTime:'1 action'}],knownSpells:[{id:'mind-sliver',name:'Mind Sliver',level:0,school:'Enchantment',castingTime:'1 action'}],resources:[{Id:'psi',Name:'Psi Points',Current:5,Maximum:7}],editSections:[]
    }});
  }
  const announce=()=>{
    if(api.state.loaded)return;
    api.send({type:'ready',editor:'character-sheet-3',version:4});
    globalThis.setTimeout(announce,180);
  };
  if (!preview.has('preview')) announce();
})();
