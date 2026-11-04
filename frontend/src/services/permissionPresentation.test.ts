import { describe, expect, it } from 'vitest';
import {
  CANONICAL_ALL_PERMISSION_CODES,
  demoCustomerPermissionCatalog,
  demoPermissionCatalog,
} from '../mocks/canonicalErpWmsMockData';
import {
  permissionDescription,
  permissionLabel,
  permissionModuleGroup,
} from './permissionPresentation';

const DUPLICATE_PHRASE_RE = /(phê duyệt\s+phê duyệt|kiểm tra\s+kiểm tra)/i;

function hasRepeatedAdjacentWordsOrBigrams(text: string): boolean {
  const segments = text.toLowerCase().split(/[,()/•:—–-]+/);
  for (const segment of segments) {
    const tokens = segment.trim().split(/\s+/).filter(Boolean);
    for (let i = 0; i < tokens.length - 1; i += 1) {
      if (tokens[i].length >= 2 && tokens[i] === tokens[i + 1]) {
        return true;
      }
    }
    for (let i = 0; i < tokens.length - 3; i += 1) {
      if (`${tokens[i]} ${tokens[i + 1]}` === `${tokens[i + 2]} ${tokens[i + 3]}`) {
        return true;
      }
    }
  }
  return false;
}

describe('permissionPresentation Vietnamese labels & zero duplicate words (FND-W19-02 / N-02)', () => {
  it('maps approval.approve and approval.reject cleanly without duplicate words', () => {
    expect(permissionLabel('approval.approve')).toBe('Phê duyệt phiếu nhập kho');
    expect(permissionLabel('approval.reject')).toBe('Từ chối phê duyệt phiếu nhập kho');
    expect(permissionLabel('approval.approve')).not.toMatch(DUPLICATE_PHRASE_RE);
    expect(hasRepeatedAdjacentWordsOrBigrams('Phê duyệt phê duyệt phiếu nhập')).toBe(true);
    expect(hasRepeatedAdjacentWordsOrBigrams('Kiểm tra kiểm tra chất lượng')).toBe(true);
    expect(hasRepeatedAdjacentWordsOrBigrams(permissionLabel('approval.approve'))).toBe(false);
  });

  it('produces mapped, duplicate-free Vietnamese labels and descriptions for all 151 canonical permissions', () => {
    expect(CANONICAL_ALL_PERMISSION_CODES.length).toBe(151);

    for (const code of CANONICAL_ALL_PERMISSION_CODES) {
      const label = permissionLabel(code);
      const description = permissionDescription(code);
      const group = permissionModuleGroup(code);

      expect(label, `Unmapped permission code: ${code}`).not.toBe('Quyền chưa có diễn giải');
      expect(label.length).toBeGreaterThan(4);
      expect(label, `Duplicate phrase in label for ${code}: ${label}`).not.toMatch(DUPLICATE_PHRASE_RE);
      expect(
        hasRepeatedAdjacentWordsOrBigrams(label),
        `Repeated adjacent word/bigram in label for ${code}: ${label}`,
      ).toBe(false);
      expect(description, `Duplicate phrase in description for ${code}`).not.toMatch(DUPLICATE_PHRASE_RE);
      expect(group.length).toBeGreaterThan(0);
    }

    for (const entry of [...demoPermissionCatalog, ...demoCustomerPermissionCatalog]) {
      expect(entry.description).not.toMatch(DUPLICATE_PHRASE_RE);
      expect(hasRepeatedAdjacentWordsOrBigrams(entry.description)).toBe(false);
    }
  });
});
