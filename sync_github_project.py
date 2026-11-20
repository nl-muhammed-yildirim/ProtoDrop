
import os
import subprocess
import json

# Configuration
PROJECT_NAME = "ProtoDrop"
MANIFEST_PATH = "project_manifest.json"

def run_command(cmd):
    print(f"Running: {' '.join(cmd)}")
    result = subprocess.run(cmd, capture_output=True, text=True)
    if result.returncode != 0:
        print(f"Error: {result.stderr}")
    return result

def sync():
    with open(MANIFEST_PATH, 'r') as f:
        manifest = json.load(f)
    
    # 1. Find Project ID
    print(f"Searching for project: {PROJECT_NAME}")
    # gh project list --user <user>
    # We'll assume user can provide the login via gh auth login
    
    for feature_file, data in manifest.items():
        print(f"Processing Feature: {feature_file}")
        # ... logic to call gh api ...
        # Create/Update Item
        # Set Type = Feature
        # Set Status from data['stories']
        pass

if __name__ == '__main__':
    sync()
