/*
 * Browser-global adapter for the peer/runtime dependencies expected by the
 * official marked-hbfm UMD distribution. This file is bundled by esbuild;
 * marked-hbfm itself is deliberately not imported or rebuilt here.
 */
import lodash from 'lodash';
import * as markedApi from 'marked';
import markedExtendedTables from 'marked-extended-tables';
import markedDefinitionLists from 'marked-definition-lists';
import markedAlignedParagraphs from 'marked-alignment-paragraphs';
import markedNonbreakingSpaces from 'marked-nonbreaking-spaces';
import markedSubSuperText from 'marked-subsuper-text';
import * as markedVariables from 'marked-variables';
import * as markedSmartypantsLite from 'marked-smartypants-lite';
import * as markedGfmHeadingId from 'marked-gfm-heading-id';
import * as markedEmoji from 'marked-emoji';
import markedDiagramsMarkdeep from 'marked-diagrams-markdeep';
import * as yaml from 'js-yaml';

Object.assign(globalThis, {
  _: lodash,
  marked: markedApi,
  MarkedExtendedTables: markedExtendedTables,
  MarkedDefinitionLists: markedDefinitionLists,
  MarkedAlignedParagraphs: markedAlignedParagraphs,
  MarkedNonbreakingSpaces: markedNonbreakingSpaces,
  MarkedSubSuperText: markedSubSuperText,
  markedVariables,
  markedSmartypantsLite,
  markedGfmHeadingId,
  markedEmoji,
  MarkedDiagramsMarkdeep: markedDiagramsMarkdeep,
  jsyaml: yaml
});
