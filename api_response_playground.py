"""
api_response_playground.py

Purpose:
    A learning playground for exploring the JSON response returned by the
    Employee Accounts API.

This file is for learning Python concepts. It is not intended to be part of
the production application.
"""

import json

# ============================================================
# STEP 1 - Call the API (from main.py)
# ============================================================

response = get_employee_accounts()

# response is a STRING
print(type(response))

# ============================================================
# STEP 2 - Convert JSON text into a Python dictionary
# ============================================================

response_json = json.loads(response)

print(type(response_json))

# ============================================================
# STEP 3 - Display the top-level keys
# ============================================================

print(response_json.keys())

# Example output:
# dict_keys([
#     'btsStatusCode',
#     'responseHeader',
#     'accounts',
#     'btsStatusMessage'
# ])

# ============================================================
# STEP 4 - Read individual values
# ============================================================

print(response_json["btsStatusCode"])
print(response_json["btsStatusMessage"])

# ============================================================
# STEP 5 - Get the accounts list
# ============================================================

accounts = response_json["accounts"]

print(type(accounts))     # <class 'list'>
print(len(accounts))      # Number of accounts

# ============================================================
# STEP 6 - Get the first account
# ============================================================

first_account = accounts[0]

print(first_account)

# ============================================================
# STEP 7 - Read values from the first account
# ============================================================

print(first_account["accountType"])
print(first_account["accountNumber"])

# ============================================================
# STEP 8 - Loop through every account
# ============================================================

for account in accounts:
    print(account)

# ============================================================
# STEP 9 - Print selected fields
# ============================================================

for account in accounts:
    print(
        f'{account["accountType"]} - '
        f'{account["accountNumber"]}'
    )

# ============================================================
# STEP 10 - Filter accounts
# ============================================================

for account in accounts:
    if account["accountType"] == "CC":
        print(account["accountNumber"])

# ============================================================
# Useful Python Functions
# ============================================================

# print(...)
# type(...)
# len(...)
# json.loads(...)

# ============================================================
# Python vs C#
# ============================================================

# Python                                C#

# response = get_employee_accounts()    string response = GetEmployeeAccounts();

# response_json = json.loads(response)  JsonSerializer.Deserialize<Response>(response)

# accounts = response_json["accounts"]  response.Accounts

# len(accounts)                         accounts.Count

# accounts[0]                           accounts[0]

# first_account["accountType"]          firstAccount.AccountType

# for account in accounts:              foreach(var account in accounts)

# if account["accountType"] == "CC"     if(account.AccountType == "CC")

# ============================================================
# Notes
# ============================================================

# A Python dictionary is similar to:
# Dictionary<string, object> in C#

# A Python list is similar to:
# List<T> in C#

# Variables are created automatically
#
# name = "Rachida"
# age = 67
# is_working = True

# Functions return values
#
# response = get_employee_accounts()

# Comments
#
# Single line:
# This is a comment
#
# VS Code shortcut:
# Ctrl + /
