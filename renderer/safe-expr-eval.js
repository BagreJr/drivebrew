/*
 * Minimal, non-evaluating compatibility layer for the subset of expr-eval used
 * by marked-variables. It intentionally rejects assignment, member access,
 * arrays, user-defined functions and arbitrary JavaScript execution.
 */

const MAX_EXPRESSION_LENGTH = 512;
const MAX_TOKENS = 256;
const MAX_DEPTH = 32;

export class Parser {
  constructor() {
    this.functions = Object.assign(Object.create(null), {
      round: Math.round,
      floor: Math.floor,
      ceil: Math.ceil,
      abs: Math.abs
    });
  }

  evaluate(expression, variables = Object.create(null)) {
    const source = String(expression ?? '');
    if (source.length > MAX_EXPRESSION_LENGTH) {
      throw new Error('Expression is too long.');
    }

    const tokens = tokenize(source);
    let position = 0;

    const peek = () => tokens[position];
    const consume = (type, value = undefined) => {
      const token = tokens[position];
      if (!token || token.type !== type || (value !== undefined && token.value !== value)) {
        throw new Error(`Unexpected token at position ${token?.offset ?? source.length}.`);
      }
      position += 1;
      return token;
    };

    const parseExpression = (depth = 0) => {
      guardDepth(depth);
      let value = parseTerm(depth + 1);
      while (peek()?.type === 'operator' && ['+', '-'].includes(peek().value)) {
        const operator = consume('operator').value;
        const right = parseTerm(depth + 1);
        value = operator === '+' ? Number(value) + Number(right) : Number(value) - Number(right);
      }
      return value;
    };

    const parseTerm = (depth) => {
      guardDepth(depth);
      let value = parsePower(depth + 1);
      while (peek()?.type === 'operator' && ['*', '/', '%'].includes(peek().value)) {
        const operator = consume('operator').value;
        const right = parsePower(depth + 1);
        if (operator === '*') value = Number(value) * Number(right);
        if (operator === '/') value = Number(value) / Number(right);
        if (operator === '%') value = Number(value) % Number(right);
      }
      return value;
    };

    const parsePower = (depth) => {
      guardDepth(depth);
      let value = parseUnary(depth + 1);
      if (peek()?.type === 'operator' && peek().value === '^') {
        consume('operator', '^');
        value = Math.pow(Number(value), Number(parsePower(depth + 1)));
      }
      return value;
    };

    const parseUnary = (depth) => {
      guardDepth(depth);
      if (peek()?.type === 'operator' && ['+', '-'].includes(peek().value)) {
        const operator = consume('operator').value;
        const value = Number(parseUnary(depth + 1));
        return operator === '-' ? -value : value;
      }
      return parsePrimary(depth + 1);
    };

    const parsePrimary = (depth) => {
      guardDepth(depth);
      const token = peek();
      if (token?.type === 'number') {
        consume('number');
        return token.value;
      }

      if (token?.type === 'identifier') {
        const name = consume('identifier').value;
        if (peek()?.type === 'leftParen') {
          consume('leftParen');
          const args = [];
          if (peek()?.type !== 'rightParen') {
            do {
              args.push(parseExpression(depth + 1));
              if (peek()?.type !== 'comma') break;
              consume('comma');
            } while (true);
          }
          consume('rightParen');
          const fn = Object.prototype.hasOwnProperty.call(this.functions, name)
            ? this.functions[name]
            : undefined;
          if (typeof fn !== 'function') {
            throw new Error(`Unknown function: ${name}.`);
          }
          return fn(...args);
        }

        if (!Object.prototype.hasOwnProperty.call(variables, name)) {
          throw new Error(`Unknown variable: ${name}.`);
        }
        const value = variables[name];
        if (typeof value === 'function' || (typeof value === 'object' && value !== null)) {
          throw new Error(`Unsafe variable value: ${name}.`);
        }
        return value;
      }

      if (token?.type === 'leftParen') {
        consume('leftParen');
        const value = parseExpression(depth + 1);
        consume('rightParen');
        return value;
      }

      throw new Error(`Expected a number, function or parenthesized expression at position ${token?.offset ?? source.length}.`);
    };

    const result = parseExpression();
    consume('eof');
    return result;
  }
}

function guardDepth(depth) {
  if (depth > MAX_DEPTH) {
    throw new Error('Expression nesting is too deep.');
  }
}

function tokenize(source) {
  const tokens = [];
  let offset = 0;

  const push = (type, value, start) => {
    if (tokens.length >= MAX_TOKENS) {
      throw new Error('Expression has too many tokens.');
    }
    tokens.push({ type, value, offset: start });
  };

  while (offset < source.length) {
    const character = source[offset];
    if (/\s/u.test(character)) {
      offset += 1;
      continue;
    }

    const number = source.slice(offset).match(/^(?:\d+(?:\.\d*)?|\.\d+)(?:e[+-]?\d+)?/iu);
    if (number) {
      push('number', Number(number[0]), offset);
      offset += number[0].length;
      continue;
    }

    const identifier = source.slice(offset).match(/^[A-Za-z_][A-Za-z0-9_]*/u);
    if (identifier) {
      push('identifier', identifier[0], offset);
      offset += identifier[0].length;
      continue;
    }

    if ('+-*/%^'.includes(character)) {
      push('operator', character, offset++);
      continue;
    }
    if (character === '(') {
      push('leftParen', character, offset++);
      continue;
    }
    if (character === ')') {
      push('rightParen', character, offset++);
      continue;
    }
    if (character === ',') {
      push('comma', character, offset++);
      continue;
    }

    throw new Error(`Unsupported character at position ${offset}.`);
  }

  push('eof', '', offset);
  return tokens;
}
