import { describe, expect, it } from 'vitest'

import {
  ADMIN_FLAGS_PATH,
  ADMIN_NAV_ITEMS,
  ADMIN_OVERVIEW_PATH,
  ADMIN_SUPPRESSIONS_PATH,
  ADMIN_TRANSFERS_PATH,
  ADMIN_USERS_PATH,
  matchAdminPath,
} from './adminRoute'

describe('matchAdminPath (T-011-10)', () => {
  it('matchAdminPath_ReturnsOverview_ForOverviewPath', () => {
    expect(matchAdminPath(ADMIN_OVERVIEW_PATH)).toBe('overview')
  })

  it('matchAdminPath_ReturnsEachScreen_ForAllFivePaths', () => {
    expect(matchAdminPath(ADMIN_TRANSFERS_PATH)).toBe('transfers')
    expect(matchAdminPath(ADMIN_USERS_PATH)).toBe('users')
    expect(matchAdminPath(ADMIN_FLAGS_PATH)).toBe('flags')
    expect(matchAdminPath(ADMIN_SUPPRESSIONS_PATH)).toBe('suppressions')
  })

  it('matchAdminPath_ReturnsNull_ForBareAdminPath', () => {
    expect(matchAdminPath('/admin')).toBeNull()
  })

  it('matchAdminPath_ReturnsNull_ForNonAdminPaths', () => {
    expect(matchAdminPath('/')).toBeNull()
    expect(matchAdminPath('/files')).toBeNull()
    expect(matchAdminPath('/account')).toBeNull()
    expect(matchAdminPath('/t/abc12345')).toBeNull()
  })

  it('ADMIN_NAV_ITEMS_HasAllFiveItems_InOrder', () => {
    expect(ADMIN_NAV_ITEMS.map((i) => i.label)).toEqual([
      'Overview',
      'Transfers',
      'Users',
      'Flags',
      'Suppressions',
    ])
  })
})
