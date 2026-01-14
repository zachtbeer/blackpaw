#!/bin/bash
# Version bump script for semantic versioning
# Usage: version-bump.sh <current_version> <labels>
# Outputs: new_version and version_incremented flag

set -e

CURRENT_VERSION="$1"
LABELS="$2"

# Validate version format
if ! [[ "$CURRENT_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "::error::Invalid version format in version.txt: $CURRENT_VERSION"
  exit 1
fi

# Parse current version
IFS='.' read -r MAJOR MINOR PATCH <<< "$CURRENT_VERSION"

# Determine version increment based on labels (priority: major > minor > patch)
VERSION_INCREMENTED=false
NEW_VERSION="$CURRENT_VERSION"

if [[ "$LABELS" == *"major"* ]]; then
  MAJOR=$((MAJOR + 1))
  MINOR=0
  PATCH=0
  NEW_VERSION="$MAJOR.$MINOR.$PATCH"
  VERSION_INCREMENTED=true
  echo "Incrementing MAJOR version: $CURRENT_VERSION → $NEW_VERSION"
elif [[ "$LABELS" == *"minor"* ]]; then
  MINOR=$((MINOR + 1))
  PATCH=0
  NEW_VERSION="$MAJOR.$MINOR.$PATCH"
  VERSION_INCREMENTED=true
  echo "Incrementing MINOR version: $CURRENT_VERSION → $NEW_VERSION"
elif [[ "$LABELS" == *"patch"* ]]; then
  PATCH=$((PATCH + 1))
  NEW_VERSION="$MAJOR.$MINOR.$PATCH"
  VERSION_INCREMENTED=true
  echo "Incrementing PATCH version: $CURRENT_VERSION → $NEW_VERSION"
else
  echo "No version label found, keeping version: $CURRENT_VERSION"
fi

# Output for GitHub Actions
echo "new_version=$NEW_VERSION" >> $GITHUB_OUTPUT
echo "version_incremented=$VERSION_INCREMENTED" >> $GITHUB_OUTPUT
