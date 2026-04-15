import { css } from 'lit';

/** Box-sizing reset for shadow DOM — mirrors the global reset that can't pierce shadow boundaries. */
export const boxReset = css`
  *, *::before, *::after { box-sizing: border-box; }
`;
